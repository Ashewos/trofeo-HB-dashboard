using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents.Tests;

public sealed partial class ClaudeSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly long StartMs = Now.AddHours(-2).ToUnixTimeMilliseconds();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "trofeo-claude-src-" + Guid.NewGuid().ToString("N"));
    private readonly string _home;
    private readonly string _work;
    private readonly HashSet<int> _alive = [];

    public ClaudeSourceTests()
    {
        _home = Path.Combine(_root, "claude-home");
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(Path.Combine(_home, "sessions"));
        Directory.CreateDirectory(_work);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ClaudeSource NewSource() => new(_home, _alive.Contains);

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();

    /// <summary>Crée un dossier de travail et le fichier de session (vivant) correspondant ; rend le dossier.</summary>
    private string AddSession(int pid, string id, string project, string status, long startedAtMs, string? waitingFor = null)
    {
        var cwd = Path.Combine(_work, project);
        Directory.CreateDirectory(cwd);
        _alive.Add(pid);
        var waiting = waitingFor is null ? "" : ",\"waitingFor\":\"" + waitingFor + "\"";
        File.WriteAllText(Path.Combine(_home, "sessions", pid + ".json"),
            "{\"pid\":" + pid + ",\"sessionId\":\"" + id + "\",\"cwd\":" + JsonSerializer.Serialize(cwd)
            + ",\"startedAt\":" + startedAtMs + ",\"kind\":\"interactive\",\"status\":\"" + status
            + "\",\"statusUpdatedAt\":" + (startedAtMs + 5000) + waiting + "}");
        return cwd;
    }

    private string TranscriptPath(string cwd, string id)
    {
        var folder = Path.Combine(_home, "projects", NonAlphanumeric().Replace(cwd, "-"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, id + ".jsonl");
    }

    private static string Prompt(string text) =>
        $$"""{"isSidechain":false,"type":"user","message":{"role":"user","content":{{JsonSerializer.Serialize(text)}}},"timestamp":"2026-10-25T10:30:00Z"}""";

    private static string ReadCall(string toolId, string file) =>
        $$$"""{"isSidechain":false,"type":"assistant","message":{"model":"claude-opus-5-5","role":"assistant","content":[{"type":"tool_use","id":"{{{toolId}}}","name":"Read","input":{"file_path":{{{JsonSerializer.Serialize(file)}}}}}],"usage":{"input_tokens":2,"cache_creation_input_tokens":782,"cache_read_input_tokens":169665,"output_tokens":353}},"timestamp":"2026-10-25T10:30:05Z"}""";

    private void WriteQuotaCapture(string sessionId, DateTimeOffset at, double fiveHour, double sevenDay)
    {
        var record = new StatusLineRecord(at, sessionId, "claude-opus-5-5", null, null,
            new CapturedQuota(fiveHour, at.AddHours(3)), new CapturedQuota(sevenDay, at.AddDays(4)));
        StatusLineFile.Write(ClaudeHome.StatusLineDir(_home), record);
    }

    [Fact]
    public void Source_Poll_BuildsSnapshot()
    {
        var cwdA = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        AddSession(11, "sess-b", "projet-b", "waiting", StartMs + 60_000, waitingFor: "permission");
        File.WriteAllLines(TranscriptPath(cwdA, "sess-a"), [Prompt("Écris le plan 3"), ReadCall("t1", "/src/Foo.cs")]);
        var tasks = Path.Combine(_home, "tasks", "sess-a");
        Directory.CreateDirectory(tasks);
        File.WriteAllText(Path.Combine(tasks, "1.json"), """{"id":"1","subject":"Écrire","status":"in_progress","activeForm":"Écriture"}""");
        WriteQuotaCapture("sess-a", Now.AddSeconds(-30), 31, 12);
        using var source = NewSource();

        var snapshot = source.Poll("sess-a", Now);

        Assert.True(snapshot.Installed);
        Assert.Equal(["sess-a", "sess-b"], snapshot.Sessions.Select(s => s.Id));
        Assert.Equal(["projet-a", "projet-b"], snapshot.Sessions.Select(s => s.ProjectName));
        Assert.Equal([AgentActivity.Working, AgentActivity.Waiting], snapshot.Sessions.Select(s => s.Activity));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(StartMs), snapshot.Sessions[0].StartedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(StartMs + 5000), snapshot.Sessions[0].LastActivityAt);

        var detail = snapshot.Selected!;
        Assert.Equal("sess-a", detail.Id);
        Assert.Equal("Écris le plan 3", detail.CurrentTask);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T10:30:00Z"), detail.TaskStartedAt);
        Assert.Equal([new FeedEntry("Read", "Foo.cs")], detail.Feed);
        Assert.Equal("claude-opus-5-5", detail.Model);
        Assert.Equal(170_449 / 200_000d * 100, detail.ContextPercent!.Value, 3);
        Assert.Equal([new AgentTask("Écriture", AgentTaskState.InProgress)], detail.Tasks);

        var quotas = snapshot.Quotas!;
        Assert.Equal(31, quotas.FiveHour!.UsedPercent);
        Assert.Equal(Now.AddSeconds(-30).AddHours(3), quotas.FiveHour.ResetsAt);
        Assert.Equal(12, quotas.Weekly!.UsedPercent);
        Assert.Equal(Now.AddSeconds(-30), quotas.CapturedAt);
    }

    [Fact]
    public void Source_Poll_SessionsSortedByStart_NoSelection_NoDetail()
    {
        AddSession(10, "sess-late", "p1", "idle", StartMs + 1000);
        AddSession(11, "sess-early", "p2", "inconnu", StartMs);
        using var source = NewSource();

        var snapshot = source.Poll(null, Now);

        Assert.Equal(["sess-early", "sess-late"], snapshot.Sessions.Select(s => s.Id));
        Assert.All(snapshot.Sessions, s => Assert.Equal(AgentActivity.Idle, s.Activity));
        Assert.Null(snapshot.Selected);
        Assert.Null(snapshot.Quotas);
        Assert.Null(source.Poll("absente", Now).Selected);
    }

    [Fact]
    public void Source_Poll_UsesFreshCaptureForContext_AndFallsBackToItsModel()
    {
        var cwd = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        File.WriteAllLines(TranscriptPath(cwd, "sess-a"), [Prompt("Salut")]);
        StatusLineFile.Write(ClaudeHome.StatusLineDir(_home),
            new StatusLineRecord(Now.AddSeconds(-5), "sess-a", "claude-sonnet-5-5", 1_000_000, 42.5, null, null));
        using var source = NewSource();

        var detail = source.Poll("sess-a", Now).Selected!;

        Assert.Equal(42.5, detail.ContextPercent);
        Assert.Equal("claude-sonnet-5-5", detail.Model);
    }

    [Fact]
    public void Source_Poll_AppendsOnlyNewLines()
    {
        var cwd = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        var transcript = TranscriptPath(cwd, "sess-a");
        File.WriteAllLines(transcript, [Prompt("Premier"), ReadCall("t1", "/src/A.cs")]);
        using var source = NewSource();
        Assert.Equal([new FeedEntry("Read", "A.cs")], source.Poll("sess-a", Now).Selected!.Feed);

        File.AppendAllText(transcript, ReadCall("t2", "/src/B.cs") + "\n", Encoding.UTF8);
        var detail = source.Poll("sess-a", Now.AddSeconds(1)).Selected!;

        Assert.Equal([new FeedEntry("Read", "A.cs"), new FeedEntry("Read", "B.cs")], detail.Feed);
        Assert.Equal("Premier", detail.CurrentTask);

        // Sans ajout, rien ne se double.
        Assert.Equal(2, source.Poll("sess-a", Now.AddSeconds(2)).Selected!.Feed.Count);
    }

    [Fact]
    public void Source_Poll_SnapshotFeedIsACopy()
    {
        var cwd = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        var transcript = TranscriptPath(cwd, "sess-a");
        File.WriteAllLines(transcript, [ReadCall("t1", "/src/A.cs")]);
        using var source = NewSource();
        var first = source.Poll("sess-a", Now).Selected!;

        File.AppendAllText(transcript, ReadCall("t2", "/src/B.cs") + "\n");
        source.Poll("sess-a", Now.AddSeconds(1));

        Assert.Single(first.Feed);
    }

    [Fact]
    public void Source_Poll_TruncatedTranscript_StartsOver()
    {
        var cwd = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        var transcript = TranscriptPath(cwd, "sess-a");
        File.WriteAllLines(transcript, [Prompt("Ancienne demande"), ReadCall("t1", "/src/Vieux.cs"), ReadCall("t2", "/src/Vieux2.cs")]);
        using var source = NewSource();
        Assert.Equal(2, source.Poll("sess-a", Now).Selected!.Feed.Count);

        File.WriteAllLines(transcript, [Prompt("Nouvelle"), ReadCall("t9", "/src/Neuf.cs")]);
        var detail = source.Poll("sess-a", Now.AddSeconds(1)).Selected!;

        Assert.Equal("Nouvelle", detail.CurrentTask);
        Assert.Equal([new FeedEntry("Read", "Neuf.cs")], detail.Feed);
    }

    [Fact]
    public void Source_Poll_TasksFallBackToTodosThenEmpty()
    {
        var cwd = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        const string todoWrite = """{"isSidechain":false,"type":"assistant","message":{"model":"m","role":"assistant","content":[{"type":"tool_use","id":"w","name":"TodoWrite","input":{"todos":[{"content":"Lire","status":"pending","activeForm":"Lecture"}]}}]},"timestamp":"2026-10-25T10:30:05Z"}""";
        var transcript = TranscriptPath(cwd, "sess-a");
        File.WriteAllLines(transcript, [Prompt("Salut")]);
        using var source = NewSource();
        Assert.Empty(source.Poll("sess-a", Now).Selected!.Tasks);

        File.AppendAllText(transcript, todoWrite + "\n");

        Assert.Equal([new AgentTask("Lire", AgentTaskState.Pending)], source.Poll("sess-a", Now.AddSeconds(1)).Selected!.Tasks);

        // Un dossier de tâches présent, même vide, a priorité sur les Todos.
        Directory.CreateDirectory(Path.Combine(_home, "tasks", "sess-a"));
        Assert.Empty(source.Poll("sess-a", Now.AddSeconds(2)).Selected!.Tasks);
    }

    [Fact]
    public void Source_Poll_NoTranscriptYet_StillGivesDetail()
    {
        AddSession(10, "sess-a", "projet-a", "idle", StartMs);
        using var source = NewSource();

        var detail = source.Poll("sess-a", Now).Selected!;

        Assert.Equal("sess-a", detail.Id);
        Assert.Null(detail.CurrentTask);
        Assert.Empty(detail.Feed);
        Assert.Null(detail.ContextPercent);
    }

    [Fact]
    public void Source_Poll_SessionWhoseProcessCheckFails_DoesNotBreakOthers()
    {
        AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        AddSession(11, "sess-b", "projet-b", "busy", StartMs + 1);
        using var source = new ClaudeSource(_home, pid => pid == 11 ? throw new InvalidOperationException("boum") : _alive.Contains(pid));

        var snapshot = source.Poll("sess-a", Now);

        Assert.Equal(["sess-a"], snapshot.Sessions.Select(s => s.Id));
        Assert.NotNull(snapshot.Selected);
    }

    [Fact]
    public void Source_QuotasFromAnySession()
    {
        var cwd = AddSession(10, "sess-a", "projet-a", "busy", StartMs);
        File.WriteAllLines(TranscriptPath(cwd, "sess-a"), [Prompt("Salut")]);
        WriteQuotaCapture("sess-ancienne", Now.AddDays(-3), 80, 70);
        WriteQuotaCapture("sess-autre", Now.AddMinutes(-2), 5, 6);
        using var source = NewSource();

        var snapshot = source.Poll("sess-a", Now);

        Assert.Equal("sess-a", snapshot.Selected!.Id);
        Assert.Equal(5, snapshot.Quotas!.FiveHour!.UsedPercent);
        Assert.Equal(6, snapshot.Quotas.Weekly!.UsedPercent);
        Assert.Equal(Now.AddMinutes(-2), snapshot.Quotas.CapturedAt);
    }

    [Fact]
    public void Source_Quotas_SkipsNewerCaptureWithoutQuota_AndTooOldOnes()
    {
        StatusLineFile.Write(ClaudeHome.StatusLineDir(_home),
            new StatusLineRecord(Now.AddSeconds(-1), "sess-sans-quota", "m", null, 10, null, null));
        WriteQuotaCapture("sess-vieille", Now.AddDays(-9), 99, 99);
        using var source = NewSource();
        Assert.Null(source.Poll(null, Now).Quotas);

        WriteQuotaCapture("sess-recente", Now.AddDays(-5), 20, 30);

        var quotas = source.Poll(null, Now.AddSeconds(1)).Quotas!;
        Assert.Equal(20, quotas.FiveHour!.UsedPercent);
    }

    [Fact]
    public void Source_Housekeeping_DeletesOldCapturesAtMostOncePerHour()
    {
        WriteQuotaCapture("sess-vieille", Now.AddDays(-9), 1, 1);
        var oldFile = Path.Combine(ClaudeHome.StatusLineDir(_home), "sess-vieille.json");
        using var source = NewSource();

        source.Poll(null, Now);
        Assert.False(File.Exists(oldFile));

        WriteQuotaCapture("sess-vieille", Now.AddDays(-9), 1, 1);
        source.Poll(null, Now.AddMinutes(30));
        Assert.True(File.Exists(oldFile));

        source.Poll(null, Now.AddMinutes(61));
        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void Source_NoClaudeHome_NotInstalled()
    {
        using var source = new ClaudeSource(Path.Combine(_root, "n-existe-pas"), _ => true);

        var snapshot = source.Poll(null, Now);

        Assert.False(snapshot.Installed);
        Assert.Empty(snapshot.Sessions);
        Assert.Null(snapshot.Selected);
        Assert.Null(snapshot.Quotas);
    }

    [Fact]
    public void Source_HomeWithoutSessionsFolder_InstalledButEmpty()
    {
        Directory.Delete(Path.Combine(_home, "sessions"));
        using var source = NewSource();

        var snapshot = source.Poll(null, Now);

        Assert.True(snapshot.Installed);
        Assert.Empty(snapshot.Sessions);
    }

    [Fact]
    public void Source_IsProcessAlive_TrueForSelf_FalseForNoSuchPid()
    {
        Assert.True(ClaudeSource.IsProcessAlive(Environment.ProcessId));
        Assert.False(ClaudeSource.IsProcessAlive(int.MaxValue));
        Assert.False(ClaudeSource.IsProcessAlive(-5));
    }
}
