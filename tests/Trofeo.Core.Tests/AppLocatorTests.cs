namespace Trofeo.Core.Tests;

public class AppLocatorTests : IDisposable
{
    private const string Tfm = "net10.0-windows10.0.19041.0";
    private readonly string _root = Directory.CreateTempSubdirectory("trofeo-locator-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine([_root, .. parts])).FullName;

    [Fact]
    public void AppLocator_FindsInRepoReleaseBeforeDebug()
    {
        var debug = Touch("repo", "src", "Trofeo.Settings", "bin", "Debug", Tfm, "Trofeo.Settings.exe");
        var release = Touch("repo", "src", "Trofeo.Settings", "bin", "Release", Tfm, "Trofeo.Settings.exe");
        var start = Dir("repo", "src", "Trofeo.Display", "bin", "Release", Tfm);

        Assert.Equal(release, AppLocator.Find("Trofeo.Settings.exe", start));
        File.Delete(release);
        Assert.Equal(debug, AppLocator.Find("Trofeo.Settings.exe", start));
    }

    [Fact]
    public void AppLocator_StartFolderComesFirst()
    {
        var beside = Touch("repo", "src", "Trofeo.Display", "bin", "Release", Tfm, "Trofeo.Settings.exe");
        Touch("repo", "src", "Trofeo.Settings", "bin", "Release", Tfm, "Trofeo.Settings.exe");

        Assert.Equal(beside, AppLocator.Find("Trofeo.Settings.exe", Path.GetDirectoryName(beside)));
    }

    [Fact]
    public void AppLocator_FallsBackToParentFolders()
    {
        var file = Touch("install", "Trofeo.SensorHelper.exe");
        var start = Dir("install", "sous", "dossier");

        Assert.Equal(file, AppLocator.Find("Trofeo.SensorHelper.exe", start));
    }

    [Fact]
    public void AppLocator_RespectsMaxLevels_AndMissingIsNull()
    {
        Touch("install", "Trofeo.SensorHelper.exe");
        var start = Dir("install", "a", "b", "c");

        Assert.Null(AppLocator.Find("Trofeo.SensorHelper.exe", start, maxLevels: 2));
        Assert.NotNull(AppLocator.Find("Trofeo.SensorHelper.exe", start, maxLevels: 3));
        Assert.Null(AppLocator.Find("Introuvable.exe", start));
    }

    [Fact]
    public void AppLocator_FindDirectory_WalksUp()
    {
        var mods = Dir("repo", "mods");
        Touch("repo", "mods", ".claude-plugin", "marketplace.json");
        var start = Dir("repo", "src", "Trofeo.Display", "bin", "Release", Tfm);

        Assert.Equal(mods, AppLocator.FindDirectory("mods", start));
        Assert.Equal(Path.Combine(mods, ".claude-plugin"), AppLocator.FindDirectory("mods/.claude-plugin", start));
        Assert.Null(AppLocator.FindDirectory("pas-la", start));
        Assert.Null(AppLocator.FindDirectory("mods", start, maxLevels: 1));
    }

    [Fact]
    public void AppLocator_DefaultStart_IsTheAppFolder() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Trofeo.Core.dll"), AppLocator.Find("Trofeo.Core.dll"));
}
