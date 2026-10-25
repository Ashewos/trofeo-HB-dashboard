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
    // Dernière lecture réussie par fichier : elle ne sert que de repli quand le fichier, en cours d'écriture, ne s'analyse pas.
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.Ordinal);

    // Dead : le numéro de processus et la session qu'on a vus morts pour ce fichier. Tant que le fichier relu porte les mêmes,
    // on ne revérifie pas (un fichier orphelin ferait sinon lever Process.GetProcessById à chaque relevé). Un numéro redonné
    // par Windows à un nouveau Claude Code arrive avec une autre session : il est revérifié.
    private sealed record CachedFile(ClaudeSessionFile Session)
    {
        public (int Pid, string SessionId)? Dead { get; set; }
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
                && entry.Dead != (session.Pid, session.SessionId) && CheckAlive(session.Pid) is { } alive)
            {
                if (alive)
                {
                    live.Add(session);
                }
                else
                {
                    entry.Dead = (session.Pid, session.SessionId);
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

    /// <summary>
    /// Relit et analyse le fichier à chaque appel (la date d'écriture et la taille ne suffisent pas à voir un changement :
    /// <c>busy</c> devient <c>idle</c> sans changer la taille, et deux écritures peuvent partager la même date). S'il change
    /// et qu'on n'arrive pas à l'analyser (en cours d'écriture, verrouillé, disparu), l'ancienne lecture réussie reste.
    /// </summary>
    private CachedFile? ReadFile(string path)
    {
        _cache.TryGetValue(path, out var cached);
        var session = TryParse(path);
        if (session is null)
        {
            return cached;
        }

        // Le verdict « mort » ne vaut que pour le même numéro de processus et la même session.
        var entry = new CachedFile(session) { Dead = cached?.Dead == (session.Pid, session.SessionId) ? (session.Pid, session.SessionId) : null };
        _cache[path] = entry;
        return entry;
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
