using System.Text.Json;

namespace Trofeo.Modules.Agents.Tests;

public sealed class CodexToolScriptTests
{
    private static CodexRollout Read(string script)
    {
        var rollout = new CodexRollout();
        rollout.Apply(CodexTestLines.Item(JsonSerializer.Serialize(new { type = "custom_tool_call", name = "exec", input = script }), DateTimeOffset.UtcNow));
        return rollout;
    }

    [Fact]
    public void Exec_ShowsEveryCommandInExecutionOrder()
    {
        var rollout = Read("""
            text(await tools.exec_command({cmd:"Get-Process Trofeo.Display", shell:"powershell.exe"}));
            text(await tools.exec_command({cmd:'git status --short', shell:'/bin/bash'}));
            """);

        Assert.Equal([new FeedEntry("PowerShell", "Get-Process Trofeo.Display"), new FeedEntry("Bash", "git status --short")], rollout.Feed);
    }

    [Fact]
    public void Exec_ParsesParallelCalls_Comments_QuotedKeysAndEscapedPaths()
    {
        var rollout = Read("""
            // tools.exec_command({cmd:"fake"})
            await Promise.all([
                tools.exec_command({"cmd":"Get-Content C:\\Temp\\test.txt", "shell":"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"}),
                /* tools.exec_command({cmd:'fake 2'}) */
                tools.exec_command({command:'echo \'bonjour\'', shell:'cmd.exe'})
            ]);
            """);

        Assert.Equal([new FeedEntry("PowerShell", "Get-Content C:\\Temp\\test.txt"), new FeedEntry("CMD", "echo 'bonjour'")], rollout.Feed);
    }

    [Fact]
    public void Exec_DoesNotParseCallsInsidePatchOrOtherQuotedText()
    {
        var patch = "*** Begin Patch\n*** Update File: src/sample.cs\n@@\n-old\n+tools.exec_command({cmd:'not executed'});\n*** End Patch";
        var rollout = Read("await tools.apply_patch(" + JsonSerializer.Serialize(patch) + "); const quoted = \"tools.exec_command({cmd:'fake'})\";");

        Assert.Equal([new FeedEntry("Patch", "src/sample.cs", 1, 1)], rollout.Feed);
    }

    [Fact]
    public void Exec_AbsolutePatchPathsShowTheFileNameRatherThanTheUserDirectory()
    {
        foreach (var path in new[] { "C:/Users/example/project/src/Module.cs", "/home/example/project/src/Module.cs" })
        {
            var patch = "*** Begin Patch\n*** Update File: " + path + "\n@@\n-old\n+new\n*** End Patch";
            var rollout = Read("await tools.apply_patch(" + JsonSerializer.Serialize(patch) + ");");
            Assert.Equal([new FeedEntry("Patch", "Module.cs", 1, 1)], rollout.Feed);
        }
    }

    [Fact]
    public void Exec_DynamicCommandsAreNotEvaluatedOrInvented()
    {
        var rollout = Read("""
            await tools.exec_command({cmd:`echo ${secret}`, shell:'pwsh'});
            await tools.exec_command({cmd:'prefix ' + secret, shell:'pwsh'});
            """);

        Assert.Equal(2, rollout.Feed.Count);
        Assert.All(rollout.Feed, entry => Assert.Equal(new FeedEntry("PowerShell", "Commande dynamique"), entry));
    }

    [Fact]
    public void Exec_TemplateLiteralsAndUnicodeEscapesAreDecodedWithoutExecution()
    {
        var rollout = Read("""
            await tools.exec_command({cmd:`Get-Date`, shell:'pwsh'});
            await tools.exec_command({cmd:"echo \u00e9\x21\nexit", shell:'cmd.exe'});
            """);

        Assert.Equal([new FeedEntry("PowerShell", "Get-Date"), new FeedEntry("CMD", "echo é! exit")], rollout.Feed);
    }

    [Fact]
    public void Exec_StdinPollingDoesNotPretendANewCommandWasExecuted()
    {
        var rollout = Read("await tools.write_stdin({session_id:123,chars:''});");
        Assert.Empty(rollout.Feed);
    }

    [Fact]
    public void Exec_CommandsAndMessagesCoexistInTheFeed()
    {
        var rollout = Read("await tools.exec_command({cmd:'Get-Date',shell:'pwsh'});");
        rollout.Apply(CodexTestLines.Item("""{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Terminé"}]}""", DateTimeOffset.UtcNow));

        Assert.Equal([new FeedEntry("PowerShell", "Get-Date"), new FeedEntry("Codex", "Terminé")], rollout.Feed);
    }

    [Theory]
    [InlineData("await tools.exec_command({cmd:'unterminated")]
    [InlineData("/* unterminated tools.exec_command({cmd:'fake'})")]
    [InlineData("await tools.exec_command(")]
    public void Exec_TruncatedInputDoesNotThrow(string script)
    {
        var error = Record.Exception(() => Read(script));
        Assert.Null(error);
    }

    [Fact]
    public void NamespacedFunctionCallsStillShowTheirCommand()
    {
        var rollout = new CodexRollout();
        rollout.Apply(CodexTestLines.Call("functions.exec_command", """{"cmd":"Get-Date","shell":"pwsh"}""", DateTimeOffset.UtcNow));
        Assert.Equal([new FeedEntry("PowerShell", "Get-Date")], rollout.Feed);
    }
}
