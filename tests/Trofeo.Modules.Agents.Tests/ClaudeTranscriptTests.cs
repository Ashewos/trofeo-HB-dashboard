using System.Text.Json;

namespace Trofeo.Modules.Agents.Tests;

/// <summary>Lignes écrites au format de Claude Code 2.1 (réduites aux champs utiles).</summary>
public class ClaudeTranscriptTests
{
    private static string Prompt(string content, string timestamp = "2026-10-25T09:55:52.511Z", string extra = "") =>
        $$$"""{"parentUuid":null,"isSidechain":false,{{{extra}}}"type":"user","message":{"role":"user","content":{{{JsonSerializer.Serialize(content)}}}},"timestamp":"{{{timestamp}}}","sessionId":"s1"}""";

    private static string Assistant(string toolUses, string model = "claude-opus-5-5",
        string usage = """{"input_tokens":2,"cache_creation_input_tokens":782,"cache_read_input_tokens":169665,"output_tokens":353}""",
        string extra = "") =>
        $$$"""{"parentUuid":"p","isSidechain":false,{{{extra}}}"message":{"model":"{{{model}}}","id":"msg_1","type":"message","role":"assistant","content":[{{{toolUses}}}],"usage":{{{usage}}}},"type":"assistant","timestamp":"2026-10-25T09:55:58.031Z"}""";

    private static string ToolUse(string id, string name, string input) =>
        $$$"""{"type":"tool_use","id":"{{{id}}}","name":"{{{name}}}","input":{{{input}}},"caller":{"type":"direct"}}""";

    private static string Result(string id, string toolUseResult) =>
        $$$"""{"parentUuid":"p","isSidechain":false,"type":"user","message":{"role":"user","content":[{"tool_use_id":"{{{id}}}","type":"tool_result","content":"ok"}]},"timestamp":"2026-10-25T10:04:33.195Z","toolUseResult":{{{toolUseResult}}}}""";

    private static ClaudeTranscript Apply(params string[] lines)
    {
        var transcript = new ClaudeTranscript();
        foreach (var line in lines)
        {
            transcript.Apply(line);
        }

        return transcript;
    }

    [Fact]
    public void Prompt_SetsTaskAndStart()
    {
        var t = Apply(Prompt("\n  Écris le plan 3\navec soin", "2026-10-25T09:55:52.511Z"));

        Assert.Equal("Écris le plan 3", t.CurrentTask);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T09:55:52.511Z"), t.TaskStartedAt);
    }

    [Fact]
    public void Prompt_TextBlocks_SetsTask()
    {
        var t = Apply("""{"isSidechain":false,"type":"user","message":{"role":"user","content":[{"type":"text","text":"Corrige le bug"}]},"timestamp":"2026-10-25T09:55:52.511Z"}""");

        Assert.Equal("Corrige le bug", t.CurrentTask);
    }

    [Fact]
    public void Prompt_NewerReplacesOlder()
    {
        var t = Apply(Prompt("Premier", "2026-10-25T09:00:00Z"), Prompt("Second", "2026-10-25T09:30:00Z"));

        Assert.Equal("Second", t.CurrentTask);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T09:30:00Z"), t.TaskStartedAt);
    }

    [Fact]
    public void Prompt_LongLine_TruncatedTo200()
    {
        var t = Apply(Prompt(new string('x', 500)));

        Assert.Equal(200, t.CurrentTask!.Length);
    }

    [Fact]
    public void Prompt_CommandWrapper_Ignored()
    {
        var t = Apply(Prompt("<command-name>/clear</command-name>\n<command-message>clear</command-message>"));

        Assert.Null(t.CurrentTask);
        Assert.Null(t.TaskStartedAt);
    }

    [Fact]
    public void Prompt_Meta_Ignored()
    {
        var t = Apply(Prompt("Texte injecté par l'outil", extra: "\"isMeta\":true,"));

        Assert.Null(t.CurrentTask);
    }

    [Fact]
    public void Prompt_Interruption_Ignored()
    {
        var t = Apply(Prompt("Vraie demande"), Prompt("  [Request interrupted by user]"), Prompt("[Request interrupted by user for tool use]"));

        Assert.Equal("Vraie demande", t.CurrentTask);
    }

    [Fact]
    public void Prompt_CompactSummary_Ignored()
    {
        var t = Apply(Prompt("Vraie demande"),
            Prompt("This session is being continued from a previous conversation...", extra: "\"isCompactSummary\":true,"));

        Assert.Equal("Vraie demande", t.CurrentTask);
    }

    [Fact]
    public void Prompt_ToolResult_NotATask()
    {
        var t = Apply(Result("toolu_1", """{"type":"text"}"""),
            """{"isSidechain":false,"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"du texte"},{"type":"text","text":"suite"}]},"timestamp":"2026-10-25T09:55:52.511Z"}""");

        Assert.Null(t.CurrentTask);
    }

    [Fact]
    public void ToolUse_AddsFeedEntries()
    {
        var t = Apply(Assistant(string.Join(",",
            ToolUse("toolu_1", "Read", """{"file_path":"/home/user/src/Program.cs"}"""),
            ToolUse("toolu_2", "Bash", """{"command":"git status -sb && git log","description":"Vérifier le dépôt"}"""),
            ToolUse("toolu_3", "Grep", """{"pattern":"TODO","path":"src"}"""))));

        Assert.Equal(
            [new FeedEntry("Read", "Program.cs"), new FeedEntry("Bash", "Vérifier le dépôt"), new FeedEntry("Grep", "TODO")],
            t.Feed);
    }

    [Fact]
    public void ToolUse_UnknownTool_StillInFeed_AndTextBlocksIgnored()
    {
        var t = Apply("""{"isSidechain":false,"message":{"model":"claude-opus-5-5","content":[{"type":"text","text":"Je regarde."},{"type":"tool_use","id":"toolu_1","name":"Frobnicate","input":{"n":3,"cible":"bidule"}}]},"type":"assistant"}""");

        Assert.Equal([new FeedEntry("Frobnicate", "bidule")], t.Feed);
    }

    [Fact]
    public void EditResult_CountsAddedRemoved()
    {
        var t = Apply(
            Assistant(ToolUse("toolu_1", "Edit", """{"file_path":"C:\\repo\\a.txt","old_string":"deux","new_string":"deux bis"}""")),
            Result("toolu_1", """{"filePath":"a.txt","oldString":"deux\n","newString":"deux bis\nquatre\n","structuredPatch":[{"oldStart":1,"oldLines":3,"newStart":1,"newLines":4,"lines":[" un","-deux","+deux bis","+quatre"," trois"]}],"userModified":false}"""));

        Assert.Equal([new FeedEntry("Edit", "a.txt", 2, 1)], t.Feed);
    }

    [Fact]
    public void EditResult_SumsAllHunks()
    {
        var t = Apply(
            Assistant(ToolUse("toolu_1", "MultiEdit", """{"file_path":"a.txt","edits":[]}""")),
            Result("toolu_1", """{"structuredPatch":[{"lines":["-a","+b"]},{"lines":["+c"," d"]}]}"""));

        Assert.Equal([new FeedEntry("Edit", "a.txt", 2, 1)], t.Feed);
    }

    [Fact]
    public void Result_FirstToolUseIdGetsCounts()
    {
        var t = Apply(
            Assistant(ToolUse("toolu_1", "Edit", """{"file_path":"a.txt"}""") + "," + ToolUse("toolu_2", "Edit", """{"file_path":"b.txt"}""")),
            """{"isSidechain":false,"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_2","type":"tool_result","content":"ok"},{"tool_use_id":"toolu_1","type":"tool_result","content":"ok"}]},"toolUseResult":{"structuredPatch":[{"lines":["+x"]}]}}""");

        Assert.Equal([new FeedEntry("Edit", "a.txt"), new FeedEntry("Edit", "b.txt", 1, 0)], t.Feed);
    }

    [Fact]
    public void Result_UnknownId_OrPlainResult_Ignored()
    {
        var t = Apply(
            Assistant(ToolUse("toolu_1", "Read", """{"file_path":"a.txt"}""")),
            Result("toolu_9", """{"structuredPatch":[{"lines":["+x"]}]}"""),
            Result("toolu_1", """{"type":"text","file":{"filePath":"a.txt","numLines":99}}"""),
            Result("toolu_1", "\"Erreur : fichier absent\""));

        Assert.Equal([new FeedEntry("Read", "a.txt")], t.Feed);
    }

    [Fact]
    public void WriteCreate_CountsContentLines()
    {
        var t = Apply(
            Assistant(ToolUse("toolu_1", "Write", """{"file_path":"/tmp/n.txt","content":"un\ndeux\ntrois\n"}""")),
            Result("toolu_1", """{"type":"create","filePath":"/tmp/n.txt","content":"un\ndeux\ntrois\n","structuredPatch":[],"userModified":false}"""));

        Assert.Equal([new FeedEntry("Write", "n.txt", 3)], t.Feed);
    }

    [Fact]
    public void WriteCreate_CountedAtCreation_WithoutResult()
    {
        var t = Apply(Assistant(ToolUse("toolu_1", "Write", """{"file_path":"n.txt","content":"un\ndeux"}""")));

        Assert.Equal([new FeedEntry("Write", "n.txt", 2)], t.Feed);
    }

    [Fact]
    public void Usage_SumsContextTokens()
    {
        var t = Apply(Assistant(ToolUse("toolu_1", "Read", """{"file_path":"a"}""")));

        Assert.Equal(170_449, t.ContextTokens);
        Assert.Equal("claude-opus-5-5", t.Model);
    }

    [Fact]
    public void Usage_Missing_KeepsPreviousContext()
    {
        var t = Apply(
            Assistant(ToolUse("toolu_1", "Read", """{"file_path":"a"}""")),
            """{"isSidechain":false,"message":{"model":"claude-sonnet-5-5","content":[]},"type":"assistant"}""");

        Assert.Equal(170_449, t.ContextTokens);
        Assert.Equal("claude-sonnet-5-5", t.Model);
    }

    [Fact]
    public void Synthetic_IgnoredForModel()
    {
        var t = Apply(
            Assistant("", model: "claude-opus-5-5"),
            Assistant("", model: "<synthetic>", usage: """{"input_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}"""));

        Assert.Equal("claude-opus-5-5", t.Model);
        Assert.Equal(170_449, t.ContextTokens);
    }

    [Fact]
    public void Sidechain_Ignored()
    {
        var t = Apply(
            Prompt("Sous-agent", extra: ""),
            """{"isSidechain":true,"type":"user","message":{"role":"user","content":"Tâche du sous-agent"},"timestamp":"2026-10-25T09:55:52.511Z"}""",
            """{"isSidechain":true,"message":{"model":"claude-haiku-5-5","content":[{"type":"tool_use","id":"toolu_s","name":"Read","input":{"file_path":"x"}}],"usage":{"input_tokens":1}},"type":"assistant"}""");

        Assert.Equal("Sous-agent", t.CurrentTask);
        Assert.Empty(t.Feed);
        Assert.Null(t.Model);
        Assert.Null(t.ContextTokens);
    }

    [Fact]
    public void TodoWrite_SetsTodos_NotInFeed()
    {
        var t = Apply(Assistant(ToolUse("toolu_1", "TodoWrite", """
            {"todos":[{"content":"Écrire le plan","status":"in_progress","activeForm":"Écriture du plan"},{"content":"Relire","status":"pending","activeForm":"Relecture"},{"content":"Livrer","status":"completed","activeForm":"Livraison"}]}
            """)));

        Assert.Empty(t.Feed);
        Assert.Equal(
            [new AgentTask("Écriture du plan", AgentTaskState.InProgress), new AgentTask("Relire", AgentTaskState.Pending),
             new AgentTask("Livrer", AgentTaskState.Completed)],
            t.Todos);
    }

    [Fact]
    public void TodoWrite_LastListWins_AndNullBeforeAny()
    {
        var t = new ClaudeTranscript();
        Assert.Null(t.Todos);

        t.Apply(Assistant(ToolUse("toolu_1", "TodoWrite", """{"todos":[{"content":"A","status":"pending"}]}""")));
        t.Apply(Assistant(ToolUse("toolu_2", "TodoWrite", """{"todos":[]}""")));

        Assert.NotNull(t.Todos);
        Assert.Empty(t.Todos);
    }

    [Fact]
    public void Feed_KeepsLast50()
    {
        var t = new ClaudeTranscript();
        for (var i = 0; i < 60; i++)
        {
            t.Apply(Assistant(ToolUse($"toolu_{i}", "Read", $$"""{"file_path":"f{{i}}.cs"}""")));
        }

        Assert.Equal(50, t.Feed.Count);
        Assert.Equal("f10.cs", t.Feed[0].Target);
        Assert.Equal("f59.cs", t.Feed[^1].Target);

        // Un résultat tardif pour une entrée déjà sortie du flux est sans effet ; pour une entrée présente, il compte.
        t.Apply(Result("toolu_3", """{"structuredPatch":[{"lines":["+x"]}]}"""));
        t.Apply(Result("toolu_59", """{"structuredPatch":[{"lines":["+x"]}]}"""));
        Assert.Equal(1, t.Feed[^1].Added);
        Assert.Null(t.Feed[0].Added);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas du json")]
    [InlineData("{\"type\":\"user\",")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("{\"type\":\"inconnu\"}")]
    [InlineData("{\"type\":\"last-prompt\",\"lastPrompt\":\"x\"}")]
    [InlineData("{\"type\":\"user\",\"message\":\"pas un objet\"}")]
    [InlineData("{\"type\":\"assistant\",\"message\":{\"content\":\"du texte\"}}")]
    [InlineData("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"t\",\"name\":\"Bash\",\"input\":{\"command\":\"a\\ud83d\"}}]}}")]
    [InlineData("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"\\ude00\"},\"timestamp\":\"2026-10-25T09:55:52.511Z\"}")]
    public void BadLine_Ignored(string line)
    {
        var t = new ClaudeTranscript();

        t.Apply(line);

        Assert.Null(t.CurrentTask);
        Assert.Empty(t.Feed);
        Assert.Null(t.Model);
        Assert.Null(t.Todos);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var t = Apply(Prompt("Tâche"), Assistant(ToolUse("toolu_1", "Read", """{"file_path":"a"}""")),
            Assistant(ToolUse("toolu_2", "TodoWrite", """{"todos":[]}""")));

        t.Reset();

        Assert.Null(t.CurrentTask);
        Assert.Null(t.TaskStartedAt);
        Assert.Empty(t.Feed);
        Assert.Null(t.Model);
        Assert.Null(t.ContextTokens);
        Assert.Null(t.Todos);
    }

    [Theory]
    [InlineData("Read", """{"file_path":"C:\\repo\\src\\Program.cs"}""", "Read", "Program.cs")]
    [InlineData("Read", """{"file_path":"/home/user/a/b.txt"}""", "Read", "b.txt")]
    [InlineData("Write", """{"file_path":"/x/y.md","content":"z"}""", "Write", "y.md")]
    [InlineData("Edit", """{"file_path":"/x/y.cs"}""", "Edit", "y.cs")]
    [InlineData("MultiEdit", """{"file_path":"/x/y.cs"}""", "Edit", "y.cs")]
    [InlineData("NotebookEdit", """{"notebook_path":"/x/nb.ipynb"}""", "Edit", "nb.ipynb")]
    [InlineData("Bash", """{"command":"ls -la","description":"Lister"}""", "Bash", "Lister")]
    [InlineData("Bash", """{"command":"\n git status\nls"}""", "Bash", "git status")]
    [InlineData("Grep", """{"pattern":"foo.*bar","path":"."}""", "Grep", "foo.*bar")]
    [InlineData("Glob", """{"pattern":"**/*.cs"}""", "Glob", "**/*.cs")]
    [InlineData("WebFetch", """{"url":"https://learn.microsoft.com/fr-fr/dotnet/?a=1","prompt":"x"}""", "Web", "learn.microsoft.com")]
    [InlineData("WebSearch", """{"query":"skiasharp text"}""", "Recherche", "skiasharp text")]
    [InlineData("Task", """{"description":"Explorer le code","prompt":"long"}""", "Agent", "Explorer le code")]
    [InlineData("Agent", """{"description":"Relire","subagent_type":"x"}""", "Agent", "Relire")]
    [InlineData("Skill", """{"skill":"superpowers:brainstorming"}""", "Skill", "superpowers:brainstorming")]
    [InlineData("mcp__github__get_file_contents", """{"owner":"Ashewos","repo":"x"}""", "get_file_contents", "Ashewos")]
    [InlineData("mcp__github__list_issues", """{"perPage":5}""", "list_issues", "")]
    [InlineData("Frobnicate", """{"n":1,"texte":"bonjour","autre":"b"}""", "Frobnicate", "bonjour")]
    [InlineData("Frobnicate", """{}""", "Frobnicate", "")]
    [InlineData("Read", """{}""", "Read", "")]
    public void Describe_Table(string name, string inputJson, string tool, string target)
    {
        using var doc = JsonDocument.Parse(inputJson);

        var described = ToolTargets.Describe(name, doc.RootElement);

        Assert.NotNull(described);
        Assert.Equal(tool, described.Value.Tool);
        Assert.Equal(target, described.Value.Target);
    }

    [Theory]
    [InlineData("TodoWrite")]
    [InlineData("TaskCreate")]
    [InlineData("TaskUpdate")]
    [InlineData("TaskList")]
    [InlineData("TaskGet")]
    [InlineData("ToolSearch")]
    public void Describe_HiddenTools_Null(string name)
    {
        using var doc = JsonDocument.Parse("""{"subject":"x"}""");

        Assert.Null(ToolTargets.Describe(name, doc.RootElement));
    }

    [Fact]
    public void Describe_Target_OneLineAndTruncated()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { description = "ligne un\r\nligne deux\nfin" + new string('x', 300) }));

        var described = ToolTargets.Describe("Task", doc.RootElement)!.Value;

        Assert.StartsWith("ligne un ligne deux fin", described.Target);
        Assert.Equal(200, described.Target.Length);
    }
}
