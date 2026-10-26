using System.Globalization;
using System.Text;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Source Codex : les transcriptions <c>sessions/AAAA/MM/JJ/rollout-*.jsonl</c> des trois derniers jours, modifiées il y
/// a moins de 2 h (les 12 plus récentes), lues par la fin puis seulement les octets ajoutés. Ne contacte rien en ligne
/// et l'index local facultatif des titres. Exclut les sous-agents internes. Appelée depuis un seul thread à la fois.
/// </summary>
/// <param name="codexHome">Le dossier de configuration de Codex (voir <see cref="CodexHome.Resolve"/>).</param>
/// <param name="time">Donne le fuseau local, qui nomme les dossiers de jour.</param>
public sealed class CodexSource(string codexHome, TimeProvider time) : IAgentSource
{
    private const int MaxFiles = 12;
    private const int MaxCandidates = 128;
    private const int DaysScanned = 3;
    private const int HeadBytes = 64 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    private readonly string _sessionsDir = Path.Combine(codexHome, "sessions");
    private readonly CodexTitles _titles = new(codexHome);
    // ProjectNames remonte l'arborescence sur le disque : un seul calcul par dossier de travail.
    private readonly Dictionary<string, string> _projectNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileState> _files = new(StringComparer.Ordinal);

    private sealed class FileState(string path)
    {
        public TranscriptTail Tail { get; } = new(path);

        public CodexRollout Rollout { get; } = new();

        // La première ligne (session_meta) reste à lire : la lecture est repartie de la fin et n'a pas encore pu l'obtenir.
        public bool HeadPending { get; set; }
    }

    /// <summary>Lecteur de la première ligne d'un fichier (remplaçable par les tests).</summary>
    internal Func<string, string?> HeadReader { get; init; } = ReadFirstLine;

    private sealed record Candidate(FileInfo File, DateTimeOffset WrittenAt);

    /// <inheritdoc />
    public AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset now)
    {
        if (!Directory.Exists(codexHome))
        {
            return new AgentSnapshot(false, [], null, null);
        }

        var tracked = Candidates(now);
        _titles.Refresh();
        var sessions = new List<(AgentSessionSummary Summary, CodexRollout Rollout)>();
        foreach (var candidate in tracked)
        {
            try
            {
                var rollout = Update(candidate.File.FullName);
                if (rollout.IsInternal)
                {
                    continue;
                }

                var id = rollout.SessionId ?? Path.GetFileNameWithoutExtension(candidate.File.Name);
                var startedAt = rollout.StartedAt ?? new DateTimeOffset(candidate.File.CreationTimeUtc, TimeSpan.Zero);
                var title = _titles.Find(id) ?? rollout.Title ?? ProjectName(rollout.Cwd);
                sessions.Add((new AgentSessionSummary(id, title, rollout.Activity(now), startedAt,
                    rollout.LastEventAt ?? candidate.WrittenAt), rollout));
                if (sessions.Count == MaxFiles)
                {
                    break;
                }
            }
            catch (Exception)
            {
                // Un fichier qui pose problème ne fait perdre que lui-même : les autres sessions restent listées.
            }
        }

        sessions.Sort((a, b) =>
        {
            var byStart = a.Summary.StartedAt.CompareTo(b.Summary.StartedAt);
            return byStart != 0 ? byStart : string.CompareOrdinal(a.Summary.Id, b.Summary.Id);
        });

        // Deux conversations peuvent légitimement partager un titre ou un dossier : rendre leurs onglets distincts.
        var duplicates = sessions.GroupBy(s => s.Summary.ProjectName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < sessions.Count; i++)
        {
            var (summary, rollout) = sessions[i];
            if (duplicates.Contains(summary.ProjectName))
            {
                var suffix = summary.Id.Length > 6 ? summary.Id[^6..] : summary.Id;
                sessions[i] = (summary with { ProjectName = $"{summary.ProjectName} · {suffix}" }, rollout);
            }
        }

        AgentSessionDetail? selected = null;
        if (selectedSessionId is not null && sessions.FirstOrDefault(s => s.Summary.Id == selectedSessionId) is { Rollout: { } chosen })
        {
            selected = new AgentSessionDetail(selectedSessionId, chosen.CurrentTask, chosen.TaskStartedAt,
                [.. chosen.Feed], [.. chosen.Tasks], chosen.Model, chosen.ContextPercent, chosen.TaskFinishedAt);
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
    /// 2 h, par date décroissante (128 candidats au plus, pour retenir 12 conversations après exclusion des sous-agents).
    /// Ne parcourt que ces 3 dossiers, jamais tout <c>sessions</c>.
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

        // Le quota porte sur les conversations, pas sur les sous-agents plus récents qui les masqueraient.
        return found.OrderByDescending(c => c.WrittenAt).ThenBy(c => c.File.FullName, StringComparer.Ordinal).Take(MaxCandidates).ToList();
    }

    /// <summary>
    /// Applique les lignes nouvelles de la transcription. Après un redémarrage de la lecture, l'état est remis à zéro et
    /// la première ligne du fichier (que la fin peut omettre) est appliquée avant les autres ; si on n'a pas pu la lire
    /// (verrou passager, ligne pas encore complète), on réessaie à chaque relevé tant que la session n'a pas d'identifiant.
    /// Une ligne ou un fichier qui pose problème ne fait perdre que lui-même.
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
            var restarted = state.Tail.Restarted;
            if (restarted)
            {
                state.Rollout.Reset();
                state.HeadPending = true;
            }

            if (state.HeadPending && state.Rollout.SessionId is null && HeadReader(path) is { } head)
            {
                state.HeadPending = false;
                // Si la fin recouvre tout le fichier, la première ligne en fait déjà partie.
                if (!(restarted && lines.Count > 0 && lines[0] == head))
                {
                    ApplyLine(state.Rollout, head);
                }
            }

            foreach (var line in lines)
            {
                ApplyLine(state.Rollout, line);
            }

            if (restarted && !state.Rollout.IsInternal && state.Rollout.Feed.Count == 0
                && state.Tail.Position > 512 * 1024)
            {
                state.Rollout.RestoreConversation(CodexConversationHistory.Read(path, state.Tail.Position));
            }

            if (restarted && state.Rollout.Model is null && !state.Rollout.IsInternal)
            {
                state.Rollout.RestoreModel(CodexModelHistory.ReadLatest(path));
            }

            if (state.Rollout.SessionId is not null)
            {
                state.HeadPending = false;
            }
        }
        catch (Exception)
        {
            // Fichier verrouillé, disparu ou inattendu : on garde ce qu'on sait, et on réessaie au prochain relevé.
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

    /// <summary>
    /// La première ligne du fichier (64 Kio au plus). <c>null</c> si elle est absente, incomplète ou illisible pour
    /// l'instant : on réessaiera. Vide si elle dépasse 64 Kio (inutile de réessayer).
    /// </summary>
    internal static string? ReadFirstLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[HeadBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var end = Array.IndexOf(buffer, (byte)'\n', 0, read);
            if (end < 0)
            {
                return read == buffer.Length ? "" : null;
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
