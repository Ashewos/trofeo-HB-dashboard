using System.Text.Json;
using Trofeo.Core.Layout;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Tests;

public sealed class LayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-tests-" + Guid.NewGuid().ToString("N"));

    public LayoutStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "layout.json");

    [Fact]
    public void Load_Missing_ReturnsDefault()
    {
        var layout = new LayoutStore(FilePath).Load();

        Assert.Equal(180, layout.Rotation);
        var clock = Assert.Single(layout.Placements);
        Assert.Equal("clock", clock.TypeId);
        Assert.Equal((12, 0, 4, 2), (clock.Column, clock.Row, clock.Columns, clock.Rows));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var id = Guid.NewGuid();
        var settings = JsonDocument.Parse("""{"format":"12h"}""").RootElement.Clone();
        var layout = new DashboardLayout(0, [new ModulePlacement(id, "clock", 3, 1, 4, 2, TextSize.Large, settings)]);
        var store = new LayoutStore(FilePath);

        store.Save(layout);
        var loaded = store.Load();

        Assert.Equal(0, loaded.Rotation);
        var p = Assert.Single(loaded.Placements);
        Assert.Equal(id, p.InstanceId);
        Assert.Equal("clock", p.TypeId);
        Assert.Equal((3, 1, 4, 2), (p.Column, p.Row, p.Columns, p.Rows));
        Assert.Equal(TextSize.Large, p.TextSize);
        Assert.Equal("12h", p.Settings.GetProperty("format").GetString());
        Assert.Equal(JsonValueKind.Object, p.Settings.ValueKind);
        Assert.Contains("\"Large\"", File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_FallsBackAndKeepsBackup()
    {
        File.WriteAllText(FilePath, "{nope");

        var layout = new LayoutStore(FilePath).Load();

        Assert.Equal(180, layout.Rotation);
        Assert.Single(layout.Placements);
        Assert.Equal("{nope", File.ReadAllText(FilePath + ".bad"));
    }
}
