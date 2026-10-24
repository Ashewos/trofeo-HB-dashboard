namespace Trofeo.Sensors.Tests;

public class HelperIdentityTests
{
    [Fact]
    public void InstallDir_IsProgramFilesSensorsFolder_WithTrailingSeparator()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "trofeo-HB-dashboard", "sensors")
            + Path.DirectorySeparatorChar;
        Assert.Equal(expected, HelperIdentity.InstallDir);
    }

    [Theory]
    [InlineData("trofeo-HB-dashboard\\sensors\\Trofeo.Sensors.Helper.exe", true)]
    [InlineData("TROFEO-hb-DASHBOARD\\SENSORS\\helper.exe", true)] // casse ignorée
    [InlineData("trofeo-HB-dashboard\\sensors-evil\\helper.exe", false)] // même préfixe de nom, autre dossier
    [InlineData("trofeo-HB-dashboard\\sensors", false)] // le dossier lui-même n'est pas un exécutable installé
    [InlineData("trofeo-HB-dashboard\\sensors\\..\\evil\\helper.exe", false)] // « .. » normalisé avant la comparaison
    [InlineData("trofeo-HB-dashboard\\other\\helper.exe", false)]
    public void IsTrustedPath_AppliesPrefixRule(string relativeToProgramFiles, bool expected)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relativeToProgramFiles);
        Assert.Equal(expected, HelperIdentity.IsTrustedPath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("helper.exe")] // relatif : résolu depuis le dossier courant, jamais sous InstallDir
    [InlineData("..\\sensors\\helper.exe")]
    [InlineData("C:\\bad|path\\x.exe")]
    public void IsTrustedPath_RelativeOrInvalid_IsFalse(string path) =>
        Assert.False(HelperIdentity.IsTrustedPath(path));

    [Fact]
    public void IsTrusted_UnknownProcess_IsFalse() => Assert.False(HelperIdentity.IsTrusted(-1));

    [Fact]
    public void IsTrusted_CurrentProcess_IsFalse() => Assert.False(HelperIdentity.IsTrusted(Environment.ProcessId)); // l'hôte de tests n'est pas installé
}
