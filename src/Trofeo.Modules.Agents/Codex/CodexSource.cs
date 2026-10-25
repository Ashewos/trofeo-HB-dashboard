using System.Globalization;
using System.Text;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Source Codex : les transcriptions <c>sessions/AAAA/MM/JJ/rollout-*.jsonl</c> des trois derniers jours, modifiées il y
/// a moins de 2 h (les 12 plus récentes), lues par la fin puis seulement les octets ajoutés. Ne contacte rien en ligne
/// et ne lit que ces fichiers. Appelée depuis un seul thread à la fois.
/// </summary>
/// <param name="codexHome">Le dossier de configuration de Codex (voir <see cref="CodexHome.Resolve"/>).</param>
/// <param name="time">Donne le fuseau local, qui nomme les dossiers de jour.</param>
public sealed class CodexSource(string codexHome, TimeProvider time) : IAgentSource
{
    private const int MaxFiles = 12;
    private const int DaysScanned = 3;
    private const int HeadBytes = 64 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    private readonly string _sessionsDir = Path.Combine(codexHome, "sessions");
    // ProjectNames remonte l'arborescence sur le disque : un seul calcul par dossier de travail.
    private readonly Dictionary<string, string> _projectNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);

    private sealed class FileState(string path)
    {
        public TranscriptTail Tail { get; } = new(path);

        public CodexRollout Rollout { get; } = new();
    }

    private sealed record Candidate(FileInfo File, DateTimeOffset WrittenAt);

    /// <inheritdoc />
    public AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset now)
    {
        if (!Directory.Exists(codexHome))
        {
            return new AgentSnapshot(false, [], null, null);
        }

        var tracked = Candidates(now);
        var sessions = new List<(AgentSessionSummary Summary, CodexRollout Rollout)>();
        foreach (var candidate in tracked)
        {
            var rollout = Update(candidate.File.FullName);
            var id = rollout.SessionId ?? Path.GetFileNameWithoutExtension(candidate.File.Name);
            var startedAt = rollout.StartedAt ?? new DateTimeOffset(candidate.File.CreationTimeUtc, TimeSpan.Zero);
            sessions.Add((new AgentSessionSummary(id, ProjectName(rollout.Cwd), rollout.Activity(now), startedAt,
                rollout.LastEventAt ?? candidate.WrittenAt), rollout));
        }

        sessions.Sort((a, b) =>
        {
            var byStart = a.Summary.StartedAt.CompareTo(b.Summary.StartedAt);
            return byStart != 0 ? byStart : string.CompareOrdinal(a.Summary.Id, b.Summary.Id);
        });

        AgentSessionDetail? selected = null;
        if (selectedSessionId is not null && sessions.FirstOrDefault(s => s.Summary.Id == selectedSessionId) is { Rollout: { } chosen })
        {
            selected = new AgentSessionDetail(selectedSessionId, chosen.CurrentTask, chosen.TaskStartedAt,
                [.. chosen.Feed], [.. chosen.Tasks], chosen.Model, chosen.ContextPercent);
        }

        var quotas = sessions.Select(s => s.Rollout.Quotas).Where(q => q is not null).MaxBy(q => q!.CapturedAt);
        Forget(tracked);
        return new AgentSnapshot(true, sessions.Select(s => s.Summary).ToList(), selected, quotas);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _projectNames.Clear();
        _files.Clear();
    }

    /// <summary>
    /// Les transcriptions à suivre : celles des dossiers des 3 derniers jours (date locale) modifiées il y a moins de
    /// 2 h, les 12 plus récentes en premier. Ne parcourt que ces 3 dossiers, jamais tout <c>sessions</c>.
    /// </summary>
    private List<Candidate> Candidates(DateTimeOffset now)
    {
        var today = TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).Date;
        var found = new List<Candidate>();
        for (var back = 0; back < DaysScanned; back++)
        {
            var day = today.AddDays(-back);
            var dir = Path.Combine(_sessionsDir, day.Year.ToString("0000", CultureInfo.InvariantCulture),
                day.Month.ToString("00", CultureInfo.InvariantCulture), day.Day.ToString("00", CultureInfo.InvariantCulture));
            try
            {
                foreach (var file in new DirectoryInfo(dir).EnumerateFiles("rollout-*.jsonl"))
                {
                    var writtenAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                    if (now - writtenAt < MaxAge && file.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(new Candidate(file, writtenAt));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Dossier du jour absent ou illisible : on passe au précédent.
            }
        }

        return found.OrderByDescending(c => c.WrittenAt).ThenBy(c => c.File.FullName, StringComparer.Ordinal).Take(MaxFiles).ToList();
    }

    /// <summary>
    /// Applique les lignes nouvelles de la transcription. Après un redémarrage de la lecture, l'état est remis à zéro et
    /// la première ligne du fichier (que la fin peut omettre) est appliquée avant les autres. Une ligne ou un fichier qui
    /// pose problème ne fait perdre que lui-même.
    /// </summary>
    private CodexRollout Update(string path)
    {
        if (!_files.TryGetValue(path, out var state))
        {
            state = new FileState(path);
            _files[path] = state;
        }

        try
        {
            var lines = state.Tail.ReadNewLines();
            if (state.Tail.Restarted)
            {
                state.Rollout.Reset();
                // Si la fin recouvre tout le fichier, la première ligne en fait déjà partie.
                var head = ReadFirstLine(path);
                if (head is not null && !(lines.Count > 0 && lines[0] == head))
                {
                    ApplyLine(state.Rollout, head);
                }
            }

            foreach (var line in lines)
            {
                ApplyLine(state.Rollout, line);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fichier verrouillé ou disparu : on garde ce qu'on sait, et on réessaie au prochain relevé.
        }

        return state.Rollout;
    }

    private static void ApplyLine(CodexRollout rollout, string line)
    {
        try
        {
            rollout.Apply(line);
        }
        catch (Exception)
        {
            // Une ligne inutilisable ne doit pas faire perdre celles qui suivent.
        }
    }

    /// <summary>La première ligne du fichier (64 Kio au plus), ou <c>null</c> si elle est absente, incomplète ou trop longue.</summary>
    private static string? ReadFirstLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[HeadBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var end = Array.IndexOf(buffer, (byte)'\n', 0, read);
            if (end < 0)
            {
                return null;
            }

            var length = end > 0 && buffer[end - 1] == (byte)'\r' ? end - 1 : end;
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Les lignes de la fin comptent plus que la première : on s'en passe.
            return null;
        }
    }

    private string ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return "Codex";
        }

        if (_projectNames.TryGetValue(cwd, out var name))
        {
            return name;
        }

        try
        {
            name = ProjectNames.FromWorkingDirectory(cwd);
        }
        catch (Exception)
        {
            // Dossier illisible : on s'en tient à son nom, l'onglet reste affiché.
            var trimmed = cwd.TrimEnd('\\', '/');
            name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        }

        _projectNames[cwd] = name;
        return name;
    }

    /// <summary>Oublie l'état des transcriptions qui ne sont plus suivies.</summary>
    private void Forget(List<Candidate> tracked)
    {
        var paths = tracked.Select(c => c.File.FullName).ToHashSet(StringComparer.Ordinal);
        foreach (var path in _files.Keys.Where(path => !paths.Contains(path)).ToList())
        {
            _files.Remove(path);
        }
    }
}
