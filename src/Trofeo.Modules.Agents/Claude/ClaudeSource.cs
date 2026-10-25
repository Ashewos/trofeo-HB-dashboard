using System.ComponentModel;
using System.Diagnostics;
using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Source Claude Code : les sessions ouvertes (<c>sessions/*.json</c>), le détail de celle qui est choisie (sa
/// transcription lue par la fin, puis seulement les octets ajoutés ; ses tâches), son contexte, et les quotas relevés
/// par la barre d'état. Ne contacte rien en ligne et ne lit que les fichiers listés dans le plan. Appelée depuis un seul
/// thread à la fois.
/// </summary>
/// <param name="claudeHome">Le dossier de configuration de Claude Code (voir <see cref="ClaudeHome.Resolve"/>).</param>
/// <param name="isProcessAlive">Dit si un processus tourne encore (en production : <see cref="IsProcessAlive"/>).</param>
public sealed class ClaudeSource(string claudeHome, Func<int, bool> isProcessAlive) : IAgentSource
{
    private const int MaxDetails = 3;
    private static readonly TimeSpan CaptureMaxAge = TimeSpan.FromDays(8);
    private static readonly TimeSpan CaptureDeleteAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan LocateRetryEvery = TimeSpan.FromSeconds(5);

    private readonly ClaudeSessionDirectory _directory = new(Path.Combine(claudeHome, "sessions"), isProcessAlive);
    private readonly string _projectsDir = Path.Combine(claudeHome, "projects");
    private readonly string _statusLineDir = ClaudeHome.StatusLineDir(claudeHome);

    // ProjectNames remonte l'arborescence sur le disque : un seul calcul par dossier de travail.
    private readonly Dictionary<string, string> _projectNames = new(StringComparer.Ordinal);
    // Transcription trouvée par session ; une absence n'est cherchée à nouveau que toutes les quelques secondes.
    private readonly Dictionary<string, (string? Path, DateTimeOffset CheckedAt)> _transcripts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DetailState> _details = new(StringComparer.Ordinal);
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;
    private long _tick;

    private sealed class DetailState(string path)
    {
        public string Path { get; } = path;

        public TranscriptTail Tail { get; } = new(path);

        public ClaudeTranscript Transcript { get; } = new();

        public long LastUsed { get; set; }
    }

    /// <summary>Le processus de ce numéro tourne-t-il ? Ne lève jamais d'exception.</summary>
    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // Le processus existe mais on n'a pas le droit de l'interroger : il tourne.
            return true;
        }
    }

    /// <inheritdoc />
    public AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset now)
    {
        if (!Directory.Exists(claudeHome))
        {
            return new AgentSnapshot(false, [], null, null);
        }

        Cleanup(now);
        var files = _directory.Read();
        var captures = ReadCaptures(now);

        var sessions = files
            .OrderBy(f => f.StartedAt)
            .ThenBy(f => f.SessionId, StringComparer.Ordinal)
            .Select(f => new AgentSessionSummary(f.SessionId, ProjectName(f.Cwd), ActivityOf(f.Status), f.StartedAt, f.LastActivityAt))
            .ToList();

        AgentSessionDetail? selected = null;
        if (selectedSessionId is not null && files.FirstOrDefault(f => f.SessionId == selectedSessionId) is { } file)
        {
            selected = BuildDetail(file, captures.FirstOrDefault(c => c.SessionId == file.SessionId), now);
        }

        Forget(files);
        return new AgentSnapshot(true, sessions, selected, QuotasOf(captures));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _projectNames.Clear();
        _transcripts.Clear();
        _details.Clear();
    }

    private static AgentActivity ActivityOf(string status) => status switch
    {
        "busy" => AgentActivity.Working,
        "waiting" => AgentActivity.Waiting,
        _ => AgentActivity.Idle,
    };

    private string ProjectName(string cwd)
    {
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

    /// <summary>
    /// Le détail de la session choisie. Une transcription ou une liste de tâches qui pose problème ne fait perdre que ce
    /// détail : la session reste listée et le sondage continue.
    /// </summary>
    private AgentSessionDetail BuildDetail(ClaudeSessionFile file, StatusLineRecord? capture, DateTimeOffset now)
    {
        try
        {
            var transcript = UpdateTranscript(file, now);
            var model = transcript?.Model ?? capture?.ModelId;
            IReadOnlyList<AgentTask> tasks =
                ClaudeTaskList.Read(Path.Combine(claudeHome, "tasks", file.SessionId)) ?? transcript?.Todos ?? [];

            return new AgentSessionDetail(
                file.SessionId,
                transcript?.CurrentTask,
                transcript?.TaskStartedAt,
                [.. transcript?.Feed ?? []],
                [.. tasks],
                model,
                ContextWindow.Percent(transcript?.ContextTokens, capture?.ModelId ?? model, capture, now));
        }
        catch (Exception)
        {
            return new AgentSessionDetail(file.SessionId, null, null, [], [], capture?.ModelId, null);
        }
    }

    /// <summary>Applique à l'état de la session les lignes ajoutées à sa transcription ; <c>null</c> si elle n'existe pas encore.</summary>
    private ClaudeTranscript? UpdateTranscript(ClaudeSessionFile file, DateTimeOffset now)
    {
        var path = LocateTranscript(file, now);
        if (path is null)
        {
            _details.Remove(file.SessionId);
            return null;
        }

        if (!_details.TryGetValue(file.SessionId, out var state) || state.Path != path)
        {
            state = new DetailState(path);
            _details[file.SessionId] = state;
        }

        state.LastUsed = ++_tick;
        var lines = state.Tail.ReadNewLines();
        if (state.Tail.Restarted)
        {
            state.Transcript.Reset();
        }

        foreach (var line in lines)
        {
            state.Transcript.Apply(line);
        }

        while (_details.Count > MaxDetails)
        {
            var oldest = _details.MinBy(pair => pair.Value.LastUsed).Key;
            _details.Remove(oldest);
        }

        return state.Transcript;
    }

    private string? LocateTranscript(ClaudeSessionFile file, DateTimeOffset now)
    {
        if (_transcripts.TryGetValue(file.SessionId, out var known) && (known.Path is not null || now - known.CheckedAt < LocateRetryEvery))
        {
            return known.Path;
        }

        var path = TranscriptLocator.Find(_projectsDir, file.Cwd, file.SessionId);
        _transcripts[file.SessionId] = (path, now);
        return path;
    }

    /// <summary>Oublie ce qu'on garde pour les sessions qui ne sont plus ouvertes.</summary>
    private void Forget(IReadOnlyList<ClaudeSessionFile> live)
    {
        var ids = live.Select(f => f.SessionId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _transcripts.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _transcripts.Remove(id);
        }

        foreach (var id in _details.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _details.Remove(id);
        }
    }

    private IReadOnlyList<StatusLineRecord> ReadCaptures(DateTimeOffset now)
    {
        try
        {
            return StatusLineFile.ReadAll(_statusLineDir, now, CaptureMaxAge);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Les quotas de la capture la plus récente qui en porte au moins un, toutes sessions confondues.</summary>
    private static AgentQuotas? QuotasOf(IReadOnlyList<StatusLineRecord> captures)
    {
        // ReadAll rend les captures de la plus récente à la plus ancienne.
        var capture = captures.FirstOrDefault(c => c.FiveHour is not null || c.SevenDay is not null);
        return capture is null ? null : new AgentQuotas(Window(capture.FiveHour), Window(capture.SevenDay), capture.CapturedAt);
    }

    private static QuotaWindow? Window(CapturedQuota? quota) => quota is null ? null : new QuotaWindow(quota.UsedPercent, quota.ResetsAt);

    /// <summary>Supprime les vieilles captures, au plus une fois par heure.</summary>
    private void Cleanup(DateTimeOffset now)
    {
        if (now - _lastCleanup < CleanupEvery)
        {
            return;
        }

        _lastCleanup = now;
        try
        {
            StatusLineFile.DeleteOlderThan(_statusLineDir, now, CaptureDeleteAge);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Simple ménage : on le refera dans une heure.
        }
    }
}
