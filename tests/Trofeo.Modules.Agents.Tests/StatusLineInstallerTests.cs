using System.Text;
using System.Text.Json.Nodes;
using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents.Tests;

public sealed class StatusLineInstallerTests : IDisposable
{
    private const string Exe = "C:/x/Trofeo.StatusLine.exe";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "trofeo-inst-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private string SettingsPath => Path.Combine(_home, "settings.json");
    private string BackupPath => SettingsPath + ".trofeo-bak";
    private string PreviousPath => Path.Combine(_home, "trofeo-HB-dashboard", "statusline-previous.json");

    private StatusLineInstaller Installer() => new(_home);

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(SettingsPath, json);
    }

    private JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(SettingsPath))!;

    [Fact]
    public void Install_NoSettings_CreatesCommand()
    {
        var result = Installer().Install(Exe);

        Assert.Equal(StatusLineInstallResult.Installed, result);
        var statusLine = ReadSettings()["statusLine"]!;
        Assert.Equal("command", (string?)statusLine["type"]);
        Assert.Equal("\"C:/x/Trofeo.StatusLine.exe\"", (string?)statusLine["command"]);
        Assert.False(File.Exists(BackupPath)); // rien à sauvegarder
        Assert.Null(JsonNode.Parse(File.ReadAllText(PreviousPath))!["previous"]);
        Assert.True(Installer().IsInstalled());
    }

    [Fact]
    public void Install_WritesUtf8WithoutBom()
    {
        Installer().Install(Exe);

        var bytes = File.ReadAllBytes(SettingsPath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Empty(Directory.GetFiles(_home, "*.tmp"));
    }

    [Fact]
    public void Install_InvalidUtf8_ChangesNothing()
    {
        Directory.CreateDirectory(_home);
        byte[] bytes = [.. "{\"note\":\"caf"u8, 0xE9, .. "\"}"u8]; // 0xE9 isolé : Windows-1252, pas de l'UTF-8
        File.WriteAllBytes(SettingsPath, bytes);

        var result = Installer().Install(Exe);

        Assert.Equal(StatusLineInstallResult.InvalidSettings, result);
        Assert.Equal(bytes, File.ReadAllBytes(SettingsPath));
        Assert.False(File.Exists(BackupPath));
        Assert.False(File.Exists(PreviousPath));
    }

    [Fact]
    public void Install_Utf8Bom_IsAccepted()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllBytes(SettingsPath, [0xEF, 0xBB, 0xBF, .. "{\"model\":\"opus\"}"u8]);

        Assert.Equal(StatusLineInstallResult.Installed, Installer().Install(Exe));
        Assert.Equal("opus", (string?)ReadSettings()["model"]);
    }

    [Fact]
    public void Install_BackslashesBecomeSlashes()
    {
        Installer().Install(@"C:\Outils\Trofeo.StatusLine.exe");

        Assert.Equal("\"C:/Outils/Trofeo.StatusLine.exe\"", (string?)ReadSettings()["statusLine"]!["command"]);
    }

    [Fact]
    public void Install_KeepsOtherKeys()
    {
        WriteSettings("""
            {"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo fini"}]}]},
             "permissions":{"allow":["Bash(ls)"]}}
            """);

        Installer().Install(Exe);

        var root = ReadSettings();
        Assert.Equal("opus", (string?)root["model"]);
        Assert.Equal("echo fini", (string?)root["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal("Bash(ls)", (string?)root["permissions"]!["allow"]![0]);
        Assert.NotNull(root["statusLine"]);
    }

    [Fact]
    public void Install_CommentsAndTrailingCommas_AreAccepted()
    {
        WriteSettings("""
            {
              // mon modèle
              "model": "opus", /* fin */
            }
            """);

        var result = Installer().Install(Exe);

        Assert.Equal(StatusLineInstallResult.Installed, result);
        Assert.Equal("opus", (string?)ReadSettings()["model"]);
        Assert.Contains("// mon modèle", File.ReadAllText(BackupPath)); // la copie garde les commentaires
    }

    [Fact]
    public void Install_ChainsExistingCommand()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"bash ~/sl.sh","padding":1}}""");

        var result = Installer().Install(Exe);

        Assert.Equal(StatusLineInstallResult.Installed, result);
        var statusLine = ReadSettings()["statusLine"]!;
        Assert.Equal("\"C:/x/Trofeo.StatusLine.exe\" --tee | ( bash ~/sl.sh )", (string?)statusLine["command"]);
        Assert.Equal(1, (int?)statusLine["padding"]);
        Assert.Equal("command", (string?)statusLine["type"]);
    }

    [Fact]
    public void Install_ChainsCompoundCommand_InParentheses()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"cd ~/outils && ./sl.sh"}}""");

        Installer().Install(Exe);

        Assert.Equal(
            "\"C:/x/Trofeo.StatusLine.exe\" --tee | ( cd ~/outils && ./sl.sh )",
            (string?)ReadSettings()["statusLine"]!["command"]);
    }

    [Fact]
    public void Install_NonCommandStatusLine_IsReplacedAndRemembered()
    {
        WriteSettings("""{"statusLine":"simple"}""");

        Installer().Install(Exe);

        Assert.Equal("\"C:/x/Trofeo.StatusLine.exe\"", (string?)ReadSettings()["statusLine"]!["command"]);
        Assert.Equal("simple", (string?)JsonNode.Parse(File.ReadAllText(PreviousPath))!["previous"]);
    }

    [Fact]
    public void Install_BacksUpOriginalOnlyTheFirstTime()
    {
        const string original = """{"model":"opus"}""";
        WriteSettings(original);

        Installer().Install(Exe);
        Assert.Equal(original, File.ReadAllText(BackupPath));

        Installer().Uninstall();
        File.WriteAllText(SettingsPath, """{"model":"sonnet"}""");
        Installer().Install(Exe);

        Assert.Equal(original, File.ReadAllText(BackupPath)); // pas écrasée
    }

    [Fact]
    public void Install_Twice_AlreadyInstalled()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"bash ~/sl.sh"}}""");
        Installer().Install(Exe);
        var before = File.ReadAllBytes(SettingsPath);
        var previousBefore = File.ReadAllBytes(PreviousPath);

        var result = Installer().Install(Exe);

        Assert.Equal(StatusLineInstallResult.AlreadyInstalled, result);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(previousBefore, File.ReadAllBytes(PreviousPath)); // la commande d'origine n'est pas perdue
    }

    [Fact]
    public void Install_AlreadyInstalled_IgnoresCase()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"\"d:/a/TROFEO.STATUSLINE.exe\""}}""");

        Assert.Equal(StatusLineInstallResult.AlreadyInstalled, Installer().Install(Exe));
    }

    [Theory]
    [InlineData("{pas du json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("\"texte\"")]
    [InlineData("")]
    public void Install_InvalidJson_ChangesNothing(string content)
    {
        WriteSettings(content);
        var before = File.ReadAllBytes(SettingsPath);

        var result = Installer().Install(Exe);

        Assert.Equal(StatusLineInstallResult.InvalidSettings, result);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.False(File.Exists(BackupPath));
        Assert.False(File.Exists(PreviousPath));
        Assert.False(Installer().IsInstalled());
    }

    [Fact]
    public void Uninstall_RestoresPreviousExactly()
    {
        WriteSettings("""{"model":"opus","statusLine":{"type":"command","command":"bash ~/sl.sh","padding":2,"refreshInterval":5},"theme":"dark"}""");
        var originalNode = ReadSettings()["statusLine"]!.ToJsonString();
        Installer().Install(Exe);

        var result = Installer().Uninstall();

        Assert.Equal(StatusLineInstallResult.Uninstalled, result);
        var root = ReadSettings();
        Assert.Equal(originalNode, root["statusLine"]!.ToJsonString());
        Assert.Equal("opus", (string?)root["model"]);
        Assert.Equal("dark", (string?)root["theme"]);
        Assert.False(File.Exists(PreviousPath));
        Assert.False(Installer().IsInstalled());
    }

    [Fact]
    public void Uninstall_NoPrevious_RemovesStatusLine()
    {
        WriteSettings("""{"model":"opus"}""");
        Installer().Install(Exe);

        var result = Installer().Uninstall();

        Assert.Equal(StatusLineInstallResult.Uninstalled, result);
        var root = ReadSettings();
        Assert.False(root.ContainsKey("statusLine"));
        Assert.Equal("opus", (string?)root["model"]);
        Assert.False(File.Exists(PreviousPath));
    }

    [Fact]
    public void Uninstall_PreviousFileMissing_RemovesStatusLine()
    {
        Installer().Install(Exe);
        File.Delete(PreviousPath);

        Assert.Equal(StatusLineInstallResult.Uninstalled, Installer().Uninstall());
        Assert.False(ReadSettings().ContainsKey("statusLine"));
    }

    [Fact]
    public void Uninstall_PreviousFileMissing_ChainedCommand_ChangesNothing()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"bash ~/sl.sh"}}""");
        Installer().Install(Exe);
        File.Delete(PreviousPath);
        var before = File.ReadAllBytes(SettingsPath);

        Assert.Equal(StatusLineInstallResult.InvalidSettings, Installer().Uninstall());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath)); // la commande d'origine n'est pas jetée
        Assert.True(Installer().IsInstalled());
    }

    [Fact]
    public void Uninstall_CorruptPreviousFile_InvalidUtf8_ChangesNothing()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"bash ~/sl.sh"}}""");
        Installer().Install(Exe);
        File.WriteAllBytes(PreviousPath, [.. "{\"previous\":\"caf"u8, 0xE9, .. "\"}"u8]);
        var before = File.ReadAllBytes(SettingsPath);

        Assert.Equal(StatusLineInstallResult.InvalidSettings, Installer().Uninstall());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Uninstall_Foreign_NotInstalled()
    {
        const string foreign = """{"statusLine":{"type":"command","command":"bash ~/sl.sh"}}""";
        WriteSettings(foreign);
        var before = File.ReadAllBytes(SettingsPath);

        var result = Installer().Uninstall();

        Assert.Equal(StatusLineInstallResult.NotInstalled, result);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Uninstall_NoSettings_NotInstalled()
    {
        Assert.Equal(StatusLineInstallResult.NotInstalled, Installer().Uninstall());
    }

    [Fact]
    public void Uninstall_InvalidSettings_ChangesNothing()
    {
        WriteSettings("{pas du json");
        var before = File.ReadAllBytes(SettingsPath);

        Assert.Equal(StatusLineInstallResult.InvalidSettings, Installer().Uninstall());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Uninstall_CorruptPreviousFile_ChangesNothing()
    {
        WriteSettings("""{"statusLine":{"type":"command","command":"bash ~/sl.sh"}}""");
        Installer().Install(Exe);
        File.WriteAllText(PreviousPath, "{abîmé");
        var before = File.ReadAllBytes(SettingsPath);

        Assert.Equal(StatusLineInstallResult.InvalidSettings, Installer().Uninstall());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.True(Installer().IsInstalled());
    }

    [Fact]
    public void ClaudeHome_StatusLineDir()
    {
        Assert.Equal(
            Path.Combine("h", "trofeo-HB-dashboard", "statusline"),
            ClaudeHome.StatusLineDir("h"));
    }
}
