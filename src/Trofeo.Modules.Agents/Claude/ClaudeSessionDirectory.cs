using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Une session Claude Code vue dans <c>&lt;claudeHome&gt;/sessions/*.json</c>. <paramref name="Status"/> est le texte brut
/// du fichier (<c>busy</c>, <c>waiting</c>, <c>idle</c>…) ; <paramref name="Kind"/> vaut <c>null</c> quand le fichier n'en dit rien.
/// </summary>
public sealed record ClaudeSessionFile(int Pid, string SessionId, string Cwd, string Status,
    DateTimeOffset StartedAt, DateTimeOffset LastActivityAt, string? Kind);

/// <summary>
/// Liste les sessions Claude Code ouvertes d'après leurs fichiers <c>sessions/&lt;pid&gt;.json</c> : seules celles de
/// type interactif dont le processus tourne encore sont gardées. Un fichier que Claude Code est en train de réécrire
/// (donc illisible un instant) garde sa dernière lecture réussie. Pas de verrou : à utiliser depuis un seul thread.
/// </summary>
/// <param name="sessionsDir">Le dossier <c>sessions</c> de Claude Code ; il peut ne pas exister.</param>
/// <param name="isProcessAlive">Dit si le processus de ce numéro tourne encore.</param>
public sealed class ClaudeSessionDirectory(string sessionsDir, Func<int, bool> isProcessAlive)
{
    // Dernière lecture réussie par fichier, avec la date d'écriture et la taille au moment où on l'a faite.
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.Ordinal);

    // DeadPid : le numéro de processus qu'on a vu mort pour ce contenu de fichier. On ne revérifie pas tant que le fichier
    // ne change pas (un fichier orphelin ferait sinon lever Process.GetProcessById à chaque relevé).
    private sealed record CachedFile(DateTime WriteTimeUtc, long Length, ClaudeSessionFile? Session)
    {
        public int? DeadPid { get; set; }
    }

    /// <summary>Les sessions interactives vivantes, une seule par identifiant (la plus récemment active), sans ordre garanti.</summary>
    public IReadOnlyList<ClaudeSessionFile> Read()
    {
        string[] files;
        try
        {
            files = Directory.Exists(sessionsDir) ? Directory.GetFiles(sessionsDir, "*.json") : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var seen = new HashSet<string>(files, StringComparer.Ordinal);
        foreach (var gone in _cache.Keys.Where(path => !seen.Contains(path)).ToList())
        {
            _cache.Remove(gone);
        }

        var live = new List<ClaudeSessionFile>();
        foreach (var path in files)
        {
            var entry = ReadFile(path);
            if (entry?.Session is { } session && (session.Kind is null || session.Kind == "interactive")
                && entry.DeadPid != session.Pid && CheckAlive(session.Pid) is { } alive)
            {
                if (alive)
                {
                    live.Add(session);
                }
                else
                {
                    entry.DeadPid = session.Pid;
                }
            }
        }

        return live
            .GroupBy(session => session.SessionId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(s => s.LastActivityAt).ThenByDescending(s => s.Pid).First())
            .ToList();
    }

    /// <summary>Un identifiant de session sûr comme nom de fichier ou de dossier : <c>[A-Za-z0-9-]</c>, 1 à 64 caractères.</summary>
    internal static bool IsSafeSessionId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64)
        {
            return false;
        }

        return id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-');
    }

    /// <summary>Vivant ou mort ; <c>null</c> si la vérification échoue (la session est alors écartée cette fois-ci, sans verdict gardé).</summary>
    private bool? CheckAlive(int pid)
    {
        try
        {
            return isProcessAlive(pid);
        }
        catch (Exception)
        {
            // Une vérification qui échoue ne doit pas faire perdre les autres sessions.
            return null;
        }
    }

    /// <summary>La lecture du fichier ; s'il change et qu'on n'arrive pas à le lire, l'ancienne lecture reste.</summary>
    private CachedFile? ReadFile(string path)
    {
        _cache.TryGetValue(path, out var cached);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return cached;
            }

            if (cached is not null && cached.WriteTimeUtc == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                return cached;
            }

            var session = TryParse(path);
            // Illisible : on garde l'ancienne session, avec son verdict éventuel si c'est la même.
            var entry = new CachedFile(info.LastWriteTimeUtc, info.Length, session ?? cached?.Session)
            {
                DeadPid = session is null ? cached?.DeadPid : null,
            };
            _cache[path] = entry;
            return entry;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Verrouillé ou disparu à l'instant : on réessaiera au prochain relevé.
            return cached;
        }
    }

    private static ClaudeSessionFile? TryParse(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var sessionId = Str(root, "sessionId");
            var cwd = Str(root, "cwd");
            if (Int(root, "pid") is not { } pid || !IsSafeSessionId(sessionId) || string.IsNullOrEmpty(cwd))
            {
                return null;
            }

            var updatedAt = Millis(root, "updatedAt");
            var startedAt = Millis(root, "startedAt") ?? updatedAt;
            var lastActivity = Millis(root, "statusUpdatedAt") ?? updatedAt ?? startedAt;
            if (startedAt is null || lastActivity is null)
            {
                return null;
            }

            return new ClaudeSessionFile(pid, sessionId!, cwd, Str(root, "status") ?? "", startedAt.Value, lastActivity.Value,
                Str(root, "kind"));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException
                                      or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;

    private static DateTimeOffset? Millis(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}
