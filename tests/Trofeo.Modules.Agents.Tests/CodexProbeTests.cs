namespace Trofeo.Modules.Agents.Tests;

public sealed class CodexProbeTests
{
    private static readonly string[] Lines =
    [
        """{"timestamp":"2026-10-25T10:00:00Z","type":"session_meta","payload":{"id":"secret-id-123","cwd":"/home/alex/prive"}}""",
        """{"timestamp":"2026-10-25T10:00:01Z","type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"npm test\"]}"}}""",
        """{"timestamp":"2026-10-25T10:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":4242,"cached":{"deep":{"deeper":1}}}},"rate_limits":{"primary":{"used_percent":31,"window_minutes":300}}}}""",
        """{"timestamp":"2026-10-25T10:00:03Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"primary":{"used_percent":32,"window_minutes":300}}}}""",
        """{"timestamp":"2026-10-25T10:00:04Z","type":"event_msg","payload":{"type":"agent_message","message":"bonjour","ok":true,"items":[{"n":1}]}}""",
    ];

    [Fact]
    public void Probe_PrintsKeysNotValues()
    {
        var text = CodexProbe.Describe(Lines);

        Assert.Contains("payload.rate_limits.primary.used_percent : number", text);
        Assert.Contains("event_msg/token_count", text);
        Assert.Contains("response_item/function_call", text);
        Assert.Contains("session_meta", text);
        Assert.DoesNotContain("npm test", text);
        Assert.DoesNotContain("31", text);
        Assert.DoesNotContain("32", text);
        Assert.DoesNotContain("secret-id", text);
        Assert.DoesNotContain("prive", text);
        Assert.DoesNotContain("bonjour", text);
        Assert.DoesNotContain("4242", text);
        Assert.DoesNotContain("2026", text);
    }

    [Fact]
    public void Probe_CountsLinesAndMergesKinds()
    {
        var text = CodexProbe.Describe(Lines);

        Assert.Contains("event_msg/token_count : 2 lignes", text);
        Assert.Contains("payload.info : null|object", text);
        Assert.Contains("payload.rate_limits.primary.window_minutes : number", text);
        Assert.Contains("payload.ok : boolean", text);
        Assert.Contains("payload.message : string", text);
    }

    [Fact]
    public void Probe_StopsAtDepthThree()
    {
        var text = CodexProbe.Describe(Lines);

        Assert.Contains("payload.info.last_token_usage.cached : object", text);
        Assert.DoesNotContain("deep", text);
    }

    [Fact]
    public void Probe_ArraysAreDescribedByTheirElements()
    {
        var text = CodexProbe.Describe(Lines);

        Assert.Contains("payload.items : array", text);
        Assert.Contains("payload.items[].n : number", text);
    }

    [Fact]
    public void Probe_NeverEchoesUnusualKeysOrTypes()
    {
        var text = CodexProbe.Describe(
        [
            """{"type":"event_msg","payload":{"type":"un texte avec espaces et données perso","C:\\Users\\alex\\secret.txt":1}}""",
            "pas du json",
            "",
            "[1]",
        ]);

        Assert.DoesNotContain("perso", text);
        Assert.DoesNotContain("secret", text);
        Assert.Contains("2 lignes illisibles", text);
    }

    [Fact]
    public void Probe_NoLines_SaysSo()
    {
        Assert.Equal("Aucune ligne lisible.", CodexProbe.Describe([]));
    }
}
