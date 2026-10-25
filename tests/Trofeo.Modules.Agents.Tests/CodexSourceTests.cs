using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Trofeo.Modules.Agents.Tests;

public sealed class CodexSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "trofeo-codex-src-" + Guid.NewGuid().ToString("N"));
    private readonly string _home;
    private readonly string _work;
    private readonly FakeTimeProvider _time = new(Now);

    public CodexSourceTests()
    {
        _home = Path.Combine(_root, "codex-home");
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_work);
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CodexSource NewSource() => new(_home, _time);

    private string Project(string name)
    {
        var cwd = Path.Combine(_work, name);
        Directory.CreateDirectory(cwd);
        return cwd;
    }

    private static string At(DateTimeOffset moment) => CodexTestLines.At(moment);

    private static string Meta(string id, string cwd, DateTimeOffset at) => CodexTestLines.Meta(id, cwd, at);

    private static string Event(string type, DateTimeOffset at, string extra = "") => CodexTestLines.Event(type, at, extra);

    private static string Call(string name, string arguments, DateTimeOffset at) => CodexTestLines.Call(name, arguments, at);

    private static string Filler(int index) =>
        CodexTestLines.Item("{\"type\":\"message\",\"n\":" + index + ",\"content\":\"" + new string('x', 1000) + "\"}", Now.AddHours(-1));

    /// <summary>Écrit un fichier de transcription dans le dossier du jour (local = UTC) et fixe sa date de modification.</summary>
    private string Rollout(DateTimeOffset day, string name, DateTimeOffset modified, params string[] lines)
    {
        var dir = Path.Combine(_home, "sessions", day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Concat(lines.Select(line => line + "\n")));
        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        return path;
    }

    [Fact]
    public void Source_ScansRecentDays_IgnoresOldFiles()
    {
        var recent = Now.AddMinutes(-10);
        Rollout(Now, "rollout-today.jsonl", recent, Meta("today", Project("a"), Now.AddHours(-1)));
        Rollout(Now.AddDays(-1), "rollout-yesterday.jsonl", recent, Meta("yesterday", Project("a"), Now.AddHours(-2)));
        Rollout(Now.AddDays(-2), "rollout-two-days.jsonl", recent, Meta("two-days", Project("a"), Now.AddHours(-3)));
        // Dossier de plus de 3 jours, ou fichier modifié il y a plus de 2 h, ou autre nom : ignorés.
        Rollout(Now.AddDays(-3), "rollout-three-days.jsonl", recent, Meta("three-days", Project("a"), Now.AddHours(-4)));
        Rollout(Now, "rollout-stale.jsonl", Now.AddHours(-3), Meta("stale", Project("a"), Now.AddHours(-5)));
        Rollout(Now, "notes.jsonl", recent, Meta("notes", Project("a"), Now.AddHours(-6)));
        Rollout(Now, "rollout-other.txt", recent, Meta("other", Project("a"), Now.AddHours(-7)));

        using var source = NewSource();
        var snapshot = source.Poll(null, Now);

        Assert.True(snapshot.Installed);
        Assert.Equal(["two-days", "yesterday", "today"], snapshot.Sessions.Select(s => s.Id));
    }

    [Fact]
    public void Source_UsesLocalDate_ForDayFolders()
    {
        // 12:00 UTC = 02:00 le lendemain à +14 h : le dossier du jour est celui du 26.
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test+14", TimeSpan.FromHours(14), "test", "test"));
        var path = Rollout(Now.AddDays(1), "rollout-next.jsonl", Now.AddMinutes(-1), Meta("next", Project("a"), Now));

        using var source = NewSource();

        Assert.Equal(["next"], source.Poll(null, Now).Sessions.Select(s => s.Id));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Source_KeepsTheTwelveMostRecentFiles()
    {
        for (var i = 0; i < 15; i++)
        {
            Rollout(Now, $"rollout-{i:00}.jsonl", Now.AddMinutes(-60 + i), Meta($"s{i:00}", Project("a"), Now.AddMinutes(-60 + i)));
        }

        using var source = NewSource();
        var ids = source.Poll(null, Now).Sessions.Select(s => s.Id).ToList();

        Assert.Equal(12, ids.Count);
        Assert.DoesNotContain("s00", ids);
        Assert.DoesNotContain("s02", ids);
        Assert.Contains("s03", ids);
        Assert.Contains("s14", ids);
    }

    [Fact]
    public void Source_SummaryAndDetail()
    {
        var cwd = Project("trofeo");
        var started = Now.AddMinutes(-30);
        Rollout(Now, "rollout-a.jsonl", Now.AddSeconds(-5),
            Meta("sess-a", cwd, started),
            CodexTestLines.Line("turn_context", started.AddSeconds(1), """{"model":"gpt-5-codex"}"""),
            Event("user_message", started.AddSeconds(2), ",\"message\":\"Ajoute une sonde\\nPuis teste\""),
            Event("task_started", started.AddSeconds(3)),
            Call("shell", """{"command":["ls","-la"]}""", started.AddSeconds(4)),
            Event("token_count", Now.AddSeconds(-5), ",\"info\":{\"last_token_usage\":{\"input_tokens\":20000},\"model_context_window\":200000},\"rate_limits\":{\"primary\":{\"used_percent\":31,\"window_minutes\":300,\"resets_in_seconds\":600}}"));

        using var source = NewSource();
        var snapshot = source.Poll("sess-a", Now);

        var summary = Assert.Single(snapshot.Sessions);
        Assert.Equal("sess-a", summary.Id);
        Assert.Equal("trofeo", summary.ProjectName);
        Assert.Equal(AgentActivity.Working, summary.Activity);
        Assert.Equal(started, summary.StartedAt);
        Assert.Equal(Now.AddSeconds(-5), summary.LastActivityAt);

        var detail = snapshot.Selected!;
        Assert.Equal("sess-a", detail.Id);
        Assert.Equal("Ajoute une sonde", detail.CurrentTask);
        Assert.Equal(started.AddSeconds(2), detail.TaskStartedAt);
        Assert.Equal("gpt-5-codex", detail.Model);
        Assert.Equal(10, detail.ContextPercent);
        Assert.Equal([new FeedEntry("Shell", "ls -la")], detail.Feed);
        Assert.Equal(31, snapshot.Quotas!.FiveHour!.UsedPercent);
    }

    [Fact]
    public void Source_ProjectName_ComesFromGitRoot()
    {
        var repo = Project("depot");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var sub = Path.Combine(repo, "src", "module");
        Directory.CreateDirectory(sub);
        Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1), Meta("a", sub, Now.AddMinutes(-5)));

        using var source = NewSource();

        Assert.Equal("depot", Assert.Single(source.Poll(null, Now).Sessions).ProjectName);
    }

    [Fact]
    public void Source_WithoutMeta_UsesFileNameAsId()
    {
        Rollout(Now, "rollout-2026-10-25T10-00-00-abc.jsonl", Now.AddMinutes(-1),
            Event("task_started", Now.AddMinutes(-2)));

        using var source = NewSource();
        var summary = Assert.Single(source.Poll(null, Now).Sessions);

        Assert.Equal("rollout-2026-10-25T10-00-00-abc", summary.Id);
        Assert.Equal("Codex", summary.ProjectName);
        Assert.Equal(AgentActivity.Working, summary.Activity);
    }

    [Fact]
    public void Source_SessionsSortedByStart_QuotasFromMostRecentTokenCount()
    {
        var cwd = Project("a");
        Rollout(Now, "rollout-late.jsonl", Now.AddMinutes(-1), Meta("late", cwd, Now.AddMinutes(-10)),
            Event("token_count", Now.AddMinutes(-9), ",\"rate_limits\":{\"primary\":{\"used_percent\":50}}"));
        Rollout(Now, "rollout-early.jsonl", Now.AddMinutes(-2), Meta("early", cwd, Now.AddMinutes(-40)),
            Event("token_count", Now.AddMinutes(-3), ",\"rate_limits\":{\"primary\":{\"used_percent\":70}}"));

        using var source = NewSource();
        var snapshot = source.Poll(null, Now);

        Assert.Equal(["early", "late"], snapshot.Sessions.Select(s => s.Id));
        Assert.Equal(70, snapshot.Quotas!.FiveHour!.UsedPercent);
        Assert.Null(snapshot.Selected);
    }

    [Fact]
    public void Source_ReadsOnlyAppendedLines_AndTracksActivity()
    {
        var cwd = Project("a");
        var path = Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1), Meta("a", cwd, Now.AddMinutes(-5)),
            Event("task_started", Now.AddMinutes(-4)),
            Call("shell", """{"command":"ls"}""", Now.AddMinutes(-3)));

        using var source = NewSource();
        Assert.Single(source.Poll("a", Now).Selected!.Feed);

        File.AppendAllText(path, Event("exec_approval_request", Now.AddSeconds(-30)) + "\n");
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        var waiting = source.Poll("a", Now);
        Assert.Equal(AgentActivity.Waiting, Assert.Single(waiting.Sessions).Activity);
        Assert.Single(waiting.Selected!.Feed);

        File.AppendAllText(path, Event("task_complete", Now.AddSeconds(-10)) + "\n");
        var idle = source.Poll("a", Now);
        Assert.Equal(AgentActivity.Idle, Assert.Single(idle.Sessions).Activity);
        Assert.Equal("a", idle.Sessions[0].Id);
    }

    [Fact]
    public void Source_SnapshotListsAreCopies()
    {
        var cwd = Project("a");
        var path = Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1), Meta("a", cwd, Now.AddMinutes(-5)),
            Call("view", "{}", Now.AddMinutes(-3)));

        using var source = NewSource();
        var first = source.Poll("a", Now).Selected!;
        File.AppendAllText(path, Call("view2", "{}", Now.AddMinutes(-2)) + "\n");
        var second = source.Poll("a", Now).Selected!;

        Assert.Single(first.Feed);
        Assert.Equal(2, second.Feed.Count);
    }

    [Fact]
    public void Source_TailSkipsHead_StillHasMeta()
    {
        var cwd = Project("grand");
        var lines = new List<string> { Meta("meta-id", cwd, Now.AddMinutes(-30)) };
        lines.AddRange(Enumerable.Range(0, 700).Select(Filler));
        lines.Add(Event("task_started", Now.AddMinutes(-1)));
        var path = Rollout(Now, "rollout-big.jsonl", Now.AddSeconds(-5), [.. lines]);
        Assert.True(new FileInfo(path).Length > 512 * 1024);

        using var source = NewSource();
        var summary = Assert.Single(source.Poll(null, Now).Sessions);

        Assert.Equal("meta-id", summary.Id);
        Assert.Equal("grand", summary.ProjectName);
        Assert.Equal(AgentActivity.Working, summary.Activity);
        Assert.Equal(Now.AddMinutes(-30), summary.StartedAt);
    }

    [Fact]
    public void Source_ReplacedFile_ReadsNewHead()
    {
        var cwd = Project("a");
        var lines = new List<string> { Meta("old-id", cwd, Now.AddMinutes(-30)) };
        lines.AddRange(Enumerable.Range(0, 700).Select(Filler));
        var path = Rollout(Now, "rollout-big.jsonl", Now.AddSeconds(-5), [.. lines]);

        using var source = NewSource();
        Assert.Equal("old-id", Assert.Single(source.Poll(null, Now).Sessions).Id);

        File.WriteAllText(path, Meta("new-id", cwd, Now.AddMinutes(-2)) + "\n" + Event("task_started", Now.AddMinutes(-1)) + "\n");
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        var summary = Assert.Single(source.Poll(null, Now).Sessions);

        Assert.Equal("new-id", summary.Id);
        Assert.Equal(AgentActivity.Working, summary.Activity);
    }

    [Fact]
    public void Source_SmallFile_HeadLineNotAppliedTwice()
    {
        // Fichier sans session_meta dont la première ligne est un appel d'outil : lue par la fin, pas aussi par le début.
        Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1),
            Call("view", "{}", Now.AddMinutes(-3)));

        using var source = NewSource();
        var detail = source.Poll("rollout-a", Now).Selected!;

        Assert.Single(detail.Feed);
    }

    [Fact]
    public void Source_BadLines_DoNotStopTheOthers()
    {
        var cwd = Project("a");
        Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1), Meta("a", cwd, Now.AddMinutes(-5)),
            "{ pas du json",
            Event("user_message", Now.AddMinutes(-4), ",\"message\":\"cass\\ud83d\""),
            Event("user_message", Now.AddMinutes(-3), ",\"message\":\"Bonne demande\""));

        using var source = NewSource();

        Assert.Equal("Bonne demande", source.Poll("a", Now).Selected!.CurrentTask);
    }

    [Fact]
    public void Source_BadFile_DoesNotStopTheOthers()
    {
        var cwd = Project("a");
        // Un fichier binaire (UTF-8 invalide) est lu sans erreur et n'empêche pas les autres.
        Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1), Meta("a", cwd, Now.AddMinutes(-5)));
        var binary = Path.Combine(_home, "sessions", "2026", "10", "25", "rollout-bin.jsonl");
        File.WriteAllBytes(binary, [0xFF, 0xFE, 0x00, 0x0A, 0xC3, 0x28, 0x0A]);
        File.SetLastWriteTimeUtc(binary, Now.AddMinutes(-2).UtcDateTime);

        using var source = NewSource();
        var snapshot = source.Poll(null, Now);

        Assert.Contains(snapshot.Sessions, s => s.Id == "a");
        Assert.Equal(2, snapshot.Sessions.Count);
    }

    [Fact]
    public void Source_DropsFilesThatAgeOut()
    {
        var cwd = Project("a");
        var path = Rollout(Now, "rollout-a.jsonl", Now.AddMinutes(-1), Meta("a", cwd, Now.AddMinutes(-5)));

        using var source = NewSource();
        Assert.Single(source.Poll(null, Now).Sessions);

        Assert.Empty(source.Poll(null, Now.AddHours(3)).Sessions);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Source_NoCodexHome_NotInstalled()
    {
        using var source = new CodexSource(Path.Combine(_root, "absent"), _time);

        var snapshot = source.Poll("x", Now);

        Assert.False(snapshot.Installed);
        Assert.Empty(snapshot.Sessions);
        Assert.Null(snapshot.Selected);
        Assert.Null(snapshot.Quotas);
    }

    [Fact]
    public void Source_HomeWithoutSessions_InstalledButEmpty()
    {
        using var source = NewSource();

        var snapshot = source.Poll(null, Now);

        Assert.True(snapshot.Installed);
        Assert.Empty(snapshot.Sessions);
    }

    [Fact]
    public void CodexHome_UsesEnvironmentVariable()
    {
        var before = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", "/tmp/mon-codex");
            Assert.Equal("/tmp/mon-codex", CodexHome.Resolve());

            Environment.SetEnvironmentVariable("CODEX_HOME", null);
            Assert.EndsWith(".codex", CodexHome.Resolve());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", before);
        }
    }
}
