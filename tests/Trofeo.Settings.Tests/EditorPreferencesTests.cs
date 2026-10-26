using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

public sealed class EditorPreferencesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-settings-prefs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Preferences_Unreadable_IsDefault()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "settings-app.json");

        Assert.Equal(new EditorPreferences(), EditorPreferences.Load(file));       // absent
        File.WriteAllText(file, "{ ceci n'est pas du JSON");
        Assert.Equal(new EditorPreferences(), EditorPreferences.Load(file));       // illisible
        File.WriteAllText(file, """{"firstRunDone": "oui"}""");
        Assert.Equal(new EditorPreferences(), EditorPreferences.Load(file));       // mauvais type
    }

    [Fact]
    public void Preferences_RoundTrip()
    {
        var file = Path.Combine(_dir, "sous-dossier", "settings-app.json");
        new EditorPreferences(FirstRunDone: true, RealTimePreview: true).Save(file);

        Assert.Equal(new EditorPreferences(true, true), EditorPreferences.Load(file));
    }
}
