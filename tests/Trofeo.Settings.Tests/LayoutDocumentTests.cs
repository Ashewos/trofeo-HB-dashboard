using Trofeo.Control;
using Trofeo.Core.Layout;
using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

public sealed class LayoutDocumentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-settings-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly LayoutStore _store;
    private readonly FakeServiceLink _link = new();

    public LayoutDocumentTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "layout.json");
        _store = new LayoutStore(_file);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Document_ExternalChange_ReloadsInsteadOfOverwriting()
    {
        _store.Save(DashboardLayout.Default);
        var document = new LayoutDocument(_store, _link);
        document.Load();
        var replaced = 0;
        document.Replaced += () => replaced++;

        // Quelqu'un d'autre réécrit le fichier (rotation 0) entre Load et SaveAsync.
        var elsewhere = DashboardLayout.Default with { Rotation = 0 };
        _store.Save(elsewhere);
        var onDisk = File.ReadAllText(_file);

        var outcome = await document.SaveAsync(DashboardLayout.Default with { MaxFps = 7 });

        Assert.False(outcome.Saved);
        Assert.Equal("La disposition a changé en dehors des réglages : elle a été relue.", outcome.Message);
        Assert.Equal(1, replaced);
        Assert.Equal(onDisk, File.ReadAllText(_file));
        Assert.Equal(0, document.Current.Rotation);
        Assert.Equal(0, _link.ReloadCount);
    }

    [Fact]
    public async Task Document_Save_WritesThenAsksServiceToReload()
    {
        _store.Save(DashboardLayout.Default);
        var document = new LayoutDocument(_store, _link);
        document.Load();
        _link.OnReload = () => Task.FromResult(new Reloaded(true, null, ["Le fond a été remplacé."]));

        var layout = DashboardLayout.Default with { MaxFps = 7 };
        var outcome = await document.SaveAsync(layout);

        Assert.True(outcome.Saved);
        Assert.Null(outcome.Message);
        Assert.Equal(["Le fond a été remplacé."], outcome.Warnings);
        Assert.Equal(1, _link.ReloadCount);
        Assert.Equal(7, _store.Load().MaxFps);
        Assert.Equal(7, document.Current.MaxFps);

        // La seconde modification part de l'empreinte que la première a laissée : pas de fausse alerte.
        var second = await document.SaveAsync(layout with { MaxFps = 8 });
        Assert.True(second.Saved);
    }

    [Fact]
    public async Task Document_ServiceRefuses_ReportsReason()
    {
        _store.Save(DashboardLayout.Default);
        var document = new LayoutDocument(_store, _link);
        document.Load();
        _link.OnReload = () => Task.FromResult(new Reloaded(false, "Type de module inconnu : « meteo ».", []));

        var outcome = await document.SaveAsync(DashboardLayout.Default with { MaxFps = 5 });

        Assert.True(outcome.Saved);
        Assert.Equal("Type de module inconnu : « meteo ».", outcome.Message);
    }

    [Fact]
    public async Task Document_ServiceUnreachable_StillSaves_AndSaysWhy()
    {
        _store.Save(DashboardLayout.Default);
        var document = new LayoutDocument(_store, _link);
        document.Load();
        _link.OnReload = () => throw new ControlException("La connexion au service est coupée.");

        var outcome = await document.SaveAsync(DashboardLayout.Default with { MaxFps = 5 });

        Assert.True(outcome.Saved);
        Assert.Equal("La connexion au service est coupée.", outcome.Message);
        Assert.Equal(5, _store.Load().MaxFps);
    }
}
