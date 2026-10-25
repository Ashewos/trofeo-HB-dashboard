using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Trofeo.Modules.Agents.Tests;

public sealed class CodexDesktopTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly string _home = Path.Combine(Path.GetTempPath(), "trofeo-desktop-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(Now);

    public CodexDesktopTests()
    {
        Directory.CreateDirectory(_home);
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private static string Message(string role, string text, int seconds = 0) => CodexTestLines.Item(
        JsonSerializer.Serialize(new { type = "message", role, content = new[] { new { type = role == "user" ? "input_text" : "output_text", text } } }), Now.AddSeconds(seconds));

    private static CodexRollout Read(params string[] lines)
    {
        var result = new CodexRollout();
        foreach (var line in lines)
        {
            result.Apply(line);
        }

        return result;
    }

    private string FileFor(string id, int ageMinutes = 1, bool guardian = false, params string[] messages)
    {
        var dir = Path.Combine(_home, "sessions", "2026", "10", "25");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rollout-" + id + ".jsonl");
        var meta = CodexTestLines.Line("session_meta", Now.AddMinutes(-30), JsonSerializer.Serialize(new
        {
            id, cwd = _home, source = guardian ? (object)new { subagent = new { other = "guardian" } } : "vscode",
        }));
        File.WriteAllLines(path, new[] { meta }.Concat(messages));
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(-ageMinutes).UtcDateTime);
        return path;
    }

    [Fact]
    public void Desktop_MessagesPopulateTaskAndFeed_IgnoreDeveloperAndReasoning()
    {
        var rollout = Read(Message("developer", "instruction privee"), Message("user", "Corrige le module\nPuis teste", 1),
            Message("assistant", "Je verifie le module.", 2),
            CodexTestLines.Item("""{"type":"reasoning","summary":[{"text":"interne"}]}""", Now));

        Assert.Equal("Corrige le module", rollout.CurrentTask);
        Assert.Equal("Corrige le module", rollout.Title);
        Assert.Equal(Now.AddSeconds(1), rollout.TaskStartedAt);
        Assert.Equal([new FeedEntry("Vous", "Corrige le module Puis teste"), new FeedEntry("Codex", "Je verifie le module.")], rollout.Feed);
    }

    [Theory]
    [InlineData("<environment_context>\nsecret context\n</environment_context>")]
    [InlineData("# AGENTS.md instructions for /work\n<INSTRUCTIONS>internal</INSTRUCTIONS>")]
    [InlineData("<INSTRUCTIONS>internal</INSTRUCTIONS>")]
    public void Desktop_InjectedContextIsNotAUserRequest(string text)
    {
        var rollout = Read(Message("user", text));
        Assert.Null(rollout.CurrentTask);
        Assert.Null(rollout.TaskStartedAt);
        Assert.Empty(rollout.Feed);
    }

    [Theory]
    [InlineData("<environment_context>context</environment_context>\nVraie demande")]
    [InlineData("# Files mentioned by the user:\nimage.png\n## My request:\nVraie demande")]
    public void Desktop_ExtractsRequestAfterContext(string text)
    {
        var rollout = Read(Message("user", text));
        Assert.Equal("Vraie demande", rollout.CurrentTask);
    }

    [Fact]
    public void Desktop_DurationStartsWithoutUserEvent_StopsAndRestarts()
    {
        var rollout = Read(CodexTestLines.Event("task_started", Now, ",\"started_at\":" + Now.ToUnixTimeSeconds()),
            Message("user", "Une demande", 1),
            CodexTestLines.Event("task_complete", Now.AddMinutes(2)));

        Assert.Equal(Now, rollout.TaskStartedAt);
        Assert.Equal(Now.AddMinutes(2), rollout.TaskFinishedAt);
        Assert.Equal(AgentActivity.Idle, rollout.Activity(Now.AddMinutes(8)));
        rollout.Apply(CodexTestLines.Event("task_started", Now.AddMinutes(10)));
        Assert.Equal(Now.AddMinutes(10), rollout.TaskStartedAt);
        Assert.Null(rollout.TaskFinishedAt);
    }

    [Fact]
    public void Desktop_CompletionRecoversExactDurationWhenStartWasOutsideTail()
    {
        var rollout = Read(CodexTestLines.Event("task_complete", Now,
            ",\"started_at\":" + Now.AddMinutes(-2).ToUnixTimeSeconds()
            + ",\"completed_at\":" + Now.ToUnixTimeSeconds() + ",\"duration_ms\":119336"));

        Assert.Equal(Now, rollout.TaskFinishedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(119336), rollout.TaskFinishedAt - rollout.TaskStartedAt);
    }

    [Fact]
    public void Desktop_AbortFreezesDuration_AndResetClearsDesktopState()
    {
        var meta = CodexTestLines.Line("session_meta", Now, """{"id":"internal","source":{"subagent":{"other":"guardian"}}}""");
        var rollout = Read(meta, Message("user", "Test"), CodexTestLines.Event("turn_aborted", Now.AddSeconds(12)));
        Assert.True(rollout.IsInternal);
        Assert.Equal(Now.AddSeconds(12), rollout.TaskFinishedAt);
        rollout.Reset();
        Assert.False(rollout.IsInternal);
        Assert.Null(rollout.Title);
        Assert.Null(rollout.TaskFinishedAt);
        Assert.Null(rollout.TaskStartedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Desktop_TwinUserFormatsDoNotResetStartOrDuplicateFeed(bool desktopFirst)
    {
        var desktop = Message("user", "Test");
        var legacy = CodexTestLines.Event("user_message", Now, ",\"message\":\"Test\"");
        var rollout = desktopFirst ? Read(desktop, legacy) : Read(legacy, desktop);
        Assert.Single(rollout.Feed);
        Assert.Equal(Now, rollout.TaskStartedAt);
    }

    [Fact]
    public void Source_FiltersInternalSessionsBeforeApplyingTwelveChatLimit()
    {
        FileFor("real", ageMinutes: 30, messages: [Message("user", "Vrai chat")]);
        for (var i = 0; i < 15; i++)
        {
            FileFor("guardian-" + i, guardian: true);
        }

        using var source = new CodexSource(_home, _time);
        var summary = Assert.Single(source.Poll("real", Now).Sessions);
        Assert.Equal("real", summary.Id);
        Assert.Equal("Vrai chat", summary.ProjectName);
    }

    [Fact]
    public void Source_UsesIndexTitle_RefreshesRename_AndKeepsMessagesAndFinishedTime()
    {
        FileFor("real", messages: [Message("user", "Question"), Message("assistant", "Reponse", 1), CodexTestLines.Event("task_complete", Now.AddSeconds(2))]);
        var index = Path.Combine(_home, "session_index.jsonl");
        File.WriteAllText(index, "{bad json\n" + JsonSerializer.Serialize(new { id = "real", thread_name = "Installation ecran" }) + "\n");
        using var source = new CodexSource(_home, _time);
        var snapshot = source.Poll("real", Now);
        Assert.Equal("Installation ecran", Assert.Single(snapshot.Sessions).ProjectName);
        Assert.Equal(2, snapshot.Selected!.Feed.Count);
        Assert.Equal(Now.AddSeconds(2), snapshot.Selected.TaskFinishedAt);
        File.AppendAllText(index, JsonSerializer.Serialize(new { id = "real", thread_name = "Nouveau titre du chat" }) + "\n");
        Assert.Equal("Nouveau titre du chat", Assert.Single(source.Poll(null, Now).Sessions).ProjectName);
    }

    [Fact]
    public void Source_IdenticalTitlesHaveDistinctLabels()
    {
        FileFor("first-123456", messages: [Message("user", "Meme question")]);
        FileFor("second-789012", messages: [Message("user", "Meme question")]);
        using var source = new CodexSource(_home, _time);
        var titles = source.Poll(null, Now).Sessions.Select(s => s.ProjectName).ToList();
        Assert.Equal(2, titles.Distinct().Count());
        Assert.Contains("Meme question · 123456", titles);
        Assert.Contains("Meme question · 789012", titles);
    }
}
