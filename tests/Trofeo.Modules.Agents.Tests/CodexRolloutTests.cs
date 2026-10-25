using System.Text.Json;

namespace Trofeo.Modules.Agents.Tests;

public sealed class CodexRolloutTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset S(int seconds) => T0.AddSeconds(seconds);

    private static string Meta(string id = "sess-1", string cwd = "/work/app", int seconds = 0) =>
        CodexTestLines.Line("session_meta", S(seconds),
            "{\"id\":\"" + id + "\",\"timestamp\":\"" + CodexTestLines.At(S(seconds - 1)) + "\",\"cwd\":\"" + cwd + "\"}");

    private static string Event(string type, int seconds, string extra = "") => CodexTestLines.Event(type, S(seconds), extra);

    private static string Call(string name, string arguments, int seconds = 5) => CodexTestLines.Call(name, arguments, S(seconds));

    private static string Item(string payloadJson, int seconds = 1) => CodexTestLines.Item(payloadJson, S(seconds));

    private static CodexRollout Build(params string[] lines)
    {
        var rollout = new CodexRollout();
        foreach (var line in lines)
        {
            rollout.Apply(line);
        }

        return rollout;
    }

    [Fact]
    public void Meta_SetsIdCwdStart()
    {
        var rollout = Build(Meta("abc", "/work/app", 10));

        Assert.Equal("abc", rollout.SessionId);
        Assert.Equal("/work/app", rollout.Cwd);
        Assert.Equal(T0.AddSeconds(9), rollout.StartedAt);
        Assert.Equal(T0.AddSeconds(10), rollout.LastEventAt);
    }

    [Fact]
    public void Meta_WithoutPayloadTimestamp_UsesLineTimestamp()
    {
        var rollout = Build(CodexTestLines.Line("session_meta", S(7), """{"id":"x","cwd":"/w"}"""));

        Assert.Equal(T0.AddSeconds(7), rollout.StartedAt);
    }

    [Fact]
    public void TurnContext_SetsModelAndCwd()
    {
        var rollout = Build(Meta(cwd: "/a"),
            CodexTestLines.Line("turn_context", S(1), """{"model":"gpt-5-codex","cwd":"/b"}"""));

        Assert.Equal("gpt-5-codex", rollout.Model);
        Assert.Equal("/b", rollout.Cwd);
    }

    [Fact]
    public void UserMessage_SetsTask()
    {
        var rollout = Build(Event("user_message", 3, ",\"message\":\"\\n  Corrige le test\\nPuis pousse\""));

        Assert.Equal("Corrige le test", rollout.CurrentTask);
        Assert.Equal(T0.AddSeconds(3), rollout.TaskStartedAt);
    }

    [Fact]
    public void Shell_AddsFeedEntry()
    {
        var rollout = Build(Call("shell", """{"command":["bash","-lc","npm test"],"workdir":"/x"}"""));

        Assert.Equal([new FeedEntry("Shell", "bash -lc npm test")], rollout.Feed);
    }

    [Theory]
    [InlineData("exec_command", """{"command":"git status\nsecond line"}""", "git status second line")]
    [InlineData("local_shell", """{"command":["ls"]}""", "ls")]
    [InlineData("shell", """{"timeout":5}""", "")]
    public void Shell_OtherForms(string tool, string arguments, string expected)
    {
        var rollout = Build(Call(tool, arguments));

        Assert.Equal([new FeedEntry("Shell", expected)], rollout.Feed);
    }

    [Fact]
    public void ApplyPatch_CountsLines()
    {
        const string patch = "*** Begin Patch\n*** Update File: src/a.cs\n@@\n context\n-old1\n-old2\n+new1\n+new2\n+new3\n*** Add File: b.txt\n+x\n*** End Patch";
        var custom = Item("{\"type\":\"custom_tool_call\",\"name\":\"apply_patch\",\"input\":" + JsonSerializer.Serialize(patch) + "}");
        var viaFunction = Call("apply_patch", JsonSerializer.Serialize(new { input = patch }));

        Assert.Equal([new FeedEntry("Patch", "src/a.cs", 4, 2)], Build(custom).Feed);
        Assert.Equal([new FeedEntry("Patch", "src/a.cs", 4, 2)], Build(viaFunction).Feed);
    }

    [Fact]
    public void ApplyPatch_AddFile_UsesItsPath()
    {
        var custom = Item("""{"type":"custom_tool_call","name":"apply_patch","input":"*** Begin Patch\n*** Add File: notes/new.md\n+hello\n*** End Patch"}""");

        Assert.Equal([new FeedEntry("Patch", "notes/new.md", 1, 0)], Build(custom).Feed);
    }

    [Fact]
    public void UpdatePlan_SetsTasks()
    {
        var plan = """{"explanation":"x","plan":[{"step":"Lire","status":"completed"},{"step":"Écrire","status":"in_progress"},{"step":"Pousser","status":"pending"},{"step":"Bizarre","status":"???"}]}""";
        var rollout = Build(Call("update_plan", plan));

        Assert.Equal(
            [new AgentTask("Lire", AgentTaskState.Completed), new AgentTask("Écrire", AgentTaskState.InProgress), new AgentTask("Pousser", AgentTaskState.Pending)],
            rollout.Tasks);
        Assert.Empty(rollout.Feed);
    }

    [Fact]
    public void WebSearch_AddsFeedEntry()
    {
        var rollout = Build(Item("""{"type":"web_search_call","status":"completed","action":{"type":"search","query":"dotnet 10 release"}}"""));

        Assert.Equal([new FeedEntry("Recherche", "dotnet 10 release")], rollout.Feed);
    }

    [Fact]
    public void OtherFunctionCall_UsesItsName()
    {
        var rollout = Build(Call("view_image", """{"path":"/x.png"}"""));

        Assert.Equal([new FeedEntry("view_image", "")], rollout.Feed);
    }

    [Fact]
    public void Feed_KeepsLast50()
    {
        var rollout = new CodexRollout();
        for (var i = 0; i < 60; i++)
        {
            rollout.Apply(Call("tool" + i, "{}"));
        }

        Assert.Equal(50, rollout.Feed.Count);
        Assert.Equal("tool10", rollout.Feed[0].Tool);
        Assert.Equal("tool59", rollout.Feed[^1].Tool);
    }

    [Fact]
    public void TokenCount_SetsContextAndQuotas()
    {
        var rollout = Build(Event("token_count", 20, """
            ,"info":{"last_token_usage":{"input_tokens":50000,"output_tokens":10},"model_context_window":200000},
            "rate_limits":{"primary":{"used_percent":31,"window_minutes":300,"resets_in_seconds":600},
            "secondary":{"used_percent":12.5,"window_minutes":10080,"resets_at":1793000000}}
            """));

        Assert.Equal(25, rollout.ContextPercent);
        var quotas = rollout.Quotas!;
        Assert.Equal(T0.AddSeconds(20), quotas.CapturedAt);
        Assert.Equal(new QuotaWindow(31, T0.AddSeconds(20).AddMinutes(10)), quotas.FiveHour);
        Assert.Equal(new QuotaWindow(12.5, DateTimeOffset.FromUnixTimeSeconds(1793000000)), quotas.Weekly);
    }

    [Fact]
    public void TokenCount_WithoutWindowMinutes_UsesRank()
    {
        var rollout = Build(Event("token_count", 0, """
            ,"rate_limits":{"primary":{"used_percent":5},"secondary":{"used_percent":6}}
            """));

        Assert.Equal(5, rollout.Quotas!.FiveHour!.UsedPercent);
        Assert.Equal(6, rollout.Quotas.Weekly!.UsedPercent);
        Assert.Null(rollout.Quotas.FiveHour.ResetsAt);
    }

    [Fact]
    public void TokenCount_ClassifiesWindowsByDuration()
    {
        // La fenêtre « primary » de 7 jours est la hebdomadaire ; une durée intermédiaire n'est rangée nulle part.
        var rollout = Build(Event("token_count", 0, """
            ,"rate_limits":{"primary":{"used_percent":40,"window_minutes":10080},"secondary":{"used_percent":9,"window_minutes":1440}}
            """));

        Assert.Null(rollout.Quotas!.FiveHour);
        Assert.Equal(40, rollout.Quotas.Weekly!.UsedPercent);
    }

    [Fact]
    public void TokenCount_NullInfo_KeepsPreviousContext()
    {
        var rollout = Build(
            Event("token_count", 1, ",\"info\":{\"last_token_usage\":{\"input_tokens\":100},\"model_context_window\":1000}"),
            Event("token_count", 2, ",\"info\":null,\"rate_limits\":null"));

        Assert.Equal(10, rollout.ContextPercent);
        Assert.Null(rollout.Quotas);
    }

    [Fact]
    public void TokenCount_ContextIsClamped()
    {
        var rollout = Build(Event("token_count", 1, ",\"info\":{\"last_token_usage\":{\"input_tokens\":5000},\"model_context_window\":1000}"));

        Assert.Equal(100, rollout.ContextPercent);
    }

    [Fact]
    public void Activity_StartedThenComplete()
    {
        var rollout = Build(Meta(), Event("task_started", 1));
        Assert.Equal(AgentActivity.Working, rollout.Activity(T0.AddSeconds(30)));

        rollout.Apply(Event("task_complete", 40));
        Assert.Equal(AgentActivity.Idle, rollout.Activity(T0.AddSeconds(41)));
    }

    [Fact]
    public void Activity_Aborted_IsIdle()
    {
        var rollout = Build(Event("task_started", 1), Event("turn_aborted", 5));

        Assert.Equal(AgentActivity.Idle, rollout.Activity(T0.AddSeconds(6)));
    }

    [Theory]
    [InlineData("exec_approval_request")]
    [InlineData("apply_patch_approval_request")]
    [InlineData("request_user_input")]
    public void Activity_ApprovalIsWaiting(string type)
    {
        var rollout = Build(Event("task_started", 1), Event(type, 2));

        Assert.Equal(AgentActivity.Waiting, rollout.Activity(T0.AddSeconds(3)));

        rollout.Apply(Call("shell", """{"command":["ls"]}""", 4));
        Assert.Equal(AgentActivity.Working, rollout.Activity(T0.AddSeconds(5)));
    }

    [Fact]
    public void Activity_StaleStartedIsIdle()
    {
        var rollout = Build(Event("task_started", 0));

        Assert.Equal(AgentActivity.Working, rollout.Activity(T0.AddMinutes(9)));
        Assert.Equal(AgentActivity.Idle, rollout.Activity(T0.AddMinutes(11)));
    }

    [Fact]
    public void UnknownLines_Ignored()
    {
        var rollout = Build(
            Meta(),
            Event("task_started", 1),
            "",
            "   ",
            "pas du json",
            "[1,2,3]",
            "42",
            """{"timestamp":"2026-10-25T10:00:09Z","type":"mystery","payload":{"type":"task_complete"}}""",
            """{"type":"event_msg"}""",
            """{"type":"event_msg","payload":"texte"}""",
            """{"type":"event_msg","payload":{"type":7}}""",
            """{"timestamp":12,"type":"event_msg","payload":{"type":"user_message","message":5}}""",
            """{"type":"turn_context","payload":{"model":{"a":1},"cwd":3}}""",
            """{"type":"session_meta","payload":{"id":4,"cwd":[],"timestamp":false}}""",
            """{"type":"response_item","payload":{"type":"function_call","name":5,"arguments":"{}"}}""",
            """{"type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"pas du json"}}""",
            """{"type":"response_item","payload":{"type":"function_call","name":"update_plan","arguments":"[1]"}}""",
            Event("token_count", 2, ",\"info\":{\"last_token_usage\":\"x\",\"model_context_window\":0},\"rate_limits\":{\"primary\":{\"used_percent\":\"31\"},\"secondary\":3}"),
            Event("token_count", 3, ",\"info\":[],\"rate_limits\":7"));

        Assert.Equal("sess-1", rollout.SessionId);
        Assert.Equal("/work/app", rollout.Cwd);
        Assert.Null(rollout.CurrentTask);
        Assert.Null(rollout.Model);
        Assert.Null(rollout.ContextPercent);
        Assert.Null(rollout.Quotas);
        Assert.Empty(rollout.Tasks);
        // Seul l'appel « shell » aux arguments illisibles laisse une entrée, sans cible.
        Assert.Equal([new FeedEntry("Shell", "")], rollout.Feed);
        // task_started n'a pas été terminé par la ligne de type inconnu.
        Assert.Equal(AgentActivity.Working, rollout.Activity(T0.AddSeconds(4)));
    }

    [Fact]
    public void LoneSurrogate_LineIsIgnored()
    {
        var rollout = Build(Event("user_message", 1, ",\"message\":\"avant\""));

        // \ud83d seul : GetString lève InvalidOperationException, la ligne est ignorée et l'état reste celui d'avant.
        rollout.Apply(Event("user_message", 2, ",\"message\":\"cass\\ud83d\""));

        Assert.Equal("avant", rollout.CurrentTask);
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        var rollout = Build(Meta(), Event("task_started", 1), Call("shell", """{"command":"ls"}"""),
            Event("exec_approval_request", 3), Event("token_count", 4, ",\"rate_limits\":{\"primary\":{\"used_percent\":1}}"));

        rollout.Reset();

        Assert.Null(rollout.SessionId);
        Assert.Null(rollout.LastEventAt);
        Assert.Empty(rollout.Feed);
        Assert.Null(rollout.Quotas);
        Assert.Equal(AgentActivity.Idle, rollout.Activity(T0.AddSeconds(5)));
    }
}
