using System.Text.Json.Nodes;
using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents.Tests;

public sealed class StatusLineCommandTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "trofeo-cmd-" + Guid.NewGuid().ToString("N"));
    private readonly string _exe;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public StatusLineCommandTests()
    {
        Directory.CreateDirectory(_home);
        _exe = Path.Combine(_home, "Trofeo.StatusLine.exe");
        File.WriteAllText(_exe, "");
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_home, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal); // un test rend settings.json en lecture seule
        }

        Directory.Delete(_home, recursive: true);
    }

    private string SettingsPath => Path.Combine(_home, "settings.json");

    private int Run(params string[] args) => StatusLineCommand.Run(args, _home, _out, _err);

    private void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    private string? InstalledCommand() => (string?)JsonNode.Parse(File.ReadAllText(SettingsPath))!["statusLine"]!["command"];

    [Fact]
    public void Install_WritesSettingsAndSaysWhere()
    {
        var code = Run("install", _exe);

        Assert.Equal(0, code);
        Assert.Contains("Trofeo.StatusLine.exe", InstalledCommand());
        Assert.Contains("installée", _out.ToString());
        Assert.Contains(SettingsPath, _out.ToString());
        Assert.Equal("", _err.ToString());
        Assert.DoesNotContain("commentaires", _out.ToString()); // pas de fichier avant : rien à signaler
    }

    [Fact]
    public void Install_WarnsThatCommentsAreNotKept()
    {
        WriteSettings("{ // mon commentaire\n  \"theme\": \"dark\" }");

        Run("install", _exe);

        var output = _out.ToString();
        Assert.Contains("commentaires", output);
        Assert.Contains("settings.json.trofeo-bak", output);
    }

    [Fact]
    public void Install_PreviousCommandEndingWithComment_Warns()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"echo hi # ma barre"}}""");

        var code = Run("install", _exe);

        Assert.Equal(0, code);
        Assert.Contains("se termine par un commentaire", _out.ToString());
    }

    [Fact]
    public void Install_PreviousCommandWithoutComment_NoCommentWarning()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"echo \"a # b\""}}""");

        Run("install", _exe);

        Assert.DoesNotContain("se termine par un commentaire", _out.ToString());
    }

    [Fact]
    public void Install_Twice_SaysAlreadyInstalled()
    {
        Run("install", _exe);
        _out.GetStringBuilder().Clear();

        var code = Run("install", _exe);

        Assert.Equal(0, code);
        Assert.Contains("déjà", _out.ToString());
        Assert.DoesNotContain("commentaires", _out.ToString()); // rien n'a été réécrit
    }

    [Fact]
    public void Install_MovedExe_SaysUpdated()
    {
        Run("install", _exe);
        _out.GetStringBuilder().Clear();
        var moved = Path.Combine(_home, "ailleurs", "Trofeo.StatusLine.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.WriteAllText(moved, "");

        var code = Run("install", moved);

        Assert.Equal(0, code);
        Assert.Contains("mise à jour", _out.ToString());
        Assert.Contains("ailleurs", InstalledCommand());
    }

    [Fact]
    public void Install_MissingExe_FailsWithoutWriting()
    {
        var code = Run("install", Path.Combine(_home, "absent.exe"));

        Assert.Equal(2, code);
        Assert.Contains("introuvable", _err.ToString());
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Install_NoPath_PrintsUsage()
    {
        var code = Run("install");

        Assert.Equal(2, code);
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public void Install_InvalidSettings_FailsAndKeepsFile()
    {
        WriteSettings("{pas du json");

        var code = Run("install", _exe);

        Assert.Equal(1, code);
        Assert.Contains(SettingsPath, _err.ToString());
        Assert.Equal("{pas du json", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Install_ReadOnlySettings_ReportsWriteErrorWithoutStackTrace()
    {
        WriteSettings("{}");
        File.SetAttributes(SettingsPath, FileAttributes.ReadOnly);

        var code = Run("install", _exe);

        Assert.Equal(1, code);
        var error = _err.ToString();
        Assert.Contains("Impossible d'écrire", error);
        Assert.Contains(SettingsPath, error);
        Assert.DoesNotContain("   at ", error);
    }

    [Fact]
    public void Uninstall_RestoresAndReports()
    {
        Run("install", _exe);
        _out.GetStringBuilder().Clear();

        var code = Run("uninstall");

        Assert.Equal(0, code);
        Assert.Contains("retirée", _out.ToString());
        Assert.False(new StatusLineInstaller(_home).IsInstalled());
    }

    [Fact]
    public void Uninstall_NotInstalled_SaysSoAndSucceeds()
    {
        var code = Run("uninstall");

        Assert.Equal(0, code);
        Assert.Contains("pas installée", _out.ToString());
    }

    [Fact]
    public void Uninstall_ReadOnlySettings_ReportsWriteError()
    {
        Run("install", _exe);
        File.SetAttributes(SettingsPath, FileAttributes.ReadOnly);

        var code = Run("uninstall");

        Assert.Equal(1, code);
        Assert.Contains("Impossible d'écrire", _err.ToString());
    }

    [Fact]
    public void Status_ReportsBothStatesAndPath()
    {
        Assert.Equal(0, Run("status"));
        Assert.Contains("pas installée", _out.ToString());
        Assert.Contains(SettingsPath, _out.ToString());

        Run("install", _exe);
        _out.GetStringBuilder().Clear();

        Assert.Equal(0, Run("status"));
        Assert.Contains("installée", _out.ToString());
        Assert.DoesNotContain("pas installée", _out.ToString());
        Assert.Contains(SettingsPath, _out.ToString());
    }

    [Theory]
    [InlineData]
    [InlineData("inconnu")]
    [InlineData("status", "en trop")]
    [InlineData("uninstall", "en trop")]
    [InlineData("install", "a.exe", "en trop")]
    public void BadArguments_PrintUsage(params string[] args)
    {
        Assert.Equal(2, Run(args));
        Assert.Contains("Usage", _err.ToString());
    }

    [Theory]
    [InlineData("echo hi # note", true)]
    [InlineData("echo hi #note", true)]
    [InlineData("# tout est commentaire", true)]
    [InlineData("echo hi  #", true)]
    [InlineData("echo hi\n# ligne seule", true)]
    [InlineData("echo hi # a\necho ho", false)] // le commentaire s'arrête à la fin de sa ligne
    [InlineData("echo hi", false)]
    [InlineData("echo \"a # b\"", false)]
    [InlineData("echo 'a # b'", false)]
    [InlineData("echo a#b", false)]
    [InlineData("echo ${#x}", false)]
    [InlineData("echo \"a \\\" # b\"", false)] // guillemet échappé : on est encore dans la chaîne
    [InlineData("echo \"x\" # fin", true)]
    [InlineData("", false)]
    public void EndsWithShellComment_DetectsOnlyRealComments(string command, bool expected) =>
        Assert.Equal(expected, StatusLineCommand.EndsWithShellComment(command));
}
