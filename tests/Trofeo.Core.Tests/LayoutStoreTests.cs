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
        Assert.Same(DashboardLayout.Default, layout);
    }

    [Fact]
    public void Default_HasFourDataModules_NoOverlap()
    {
        var layout = DashboardLayout.Default;

        Assert.Equal(180, layout.Rotation);
        Assert.Equal(
            [("monitoring", 8, 0), ("nowplaying", 8, 2), ("clock", 12, 0), ("network", 12, 2)],
            layout.Placements.Select(p => (p.TypeId, p.Column, p.Row)));
        Assert.All(layout.Placements, p => Assert.Equal((4, 2), (p.Columns, p.Rows)));
        Assert.Equal(layout.Placements.Count, layout.Placements.Select(p => p.InstanceId).Distinct().Count());
        Assert.Equal(new Guid("5b0c5e2e-6a53-4c0b-9d0e-3f1a7c1d2b01"), layout.Placements.Single(p => p.TypeId == "clock").InstanceId);
        Assert.Equal("pc", layout.Placements.Single(p => p.TypeId == "network").Settings.GetProperty("source").GetString());
        // Les colonnes 0 à 7 restent libres pour Claude Code (plan 3).
        Assert.All(layout.Placements, p => Assert.True(p.Column >= 8));
        foreach (var a in layout.Placements)
        {
            foreach (var b in layout.Placements.Where(b => b != a))
            {
                Assert.False(LayoutValidator.Intersects(a, b), $"{a.TypeId} chevauche {b.TypeId}");
            }
        }
    }

    [Fact]
    public void Default_SurvivesSaveAndLoad()
    {
        var store = new LayoutStore(FilePath);

        store.Save(DashboardLayout.Default);
        var loaded = store.Load();

        Assert.Equal(DashboardLayout.Default.Placements.Select(p => p.InstanceId), loaded.Placements.Select(p => p.InstanceId));
        Assert.False(File.Exists(FilePath + ".bad"));
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

        Assert.Same(DashboardLayout.Default, layout);
        Assert.Equal("{nope", File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void Load_LockedFile_ReturnsDefault()
    {
        new LayoutStore(FilePath).Save(new DashboardLayout(0, []));
        using var locked = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var layout = new LayoutStore(FilePath).Load();

        Assert.Same(DashboardLayout.Default, layout);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    private const string ClockSettings = "{}";

    private static string LayoutJson(int rotation, params (int c, int r, int cs, int rs)[] placements)
    {
        var items = placements.Select(p =>
            $$"""{"InstanceId":"{{Guid.NewGuid()}}","TypeId":"clock","Column":{{p.c}},"Row":{{p.r}},"Columns":{{p.cs}},"Rows":{{p.rs}},"TextSize":"Normal","Settings":{{ClockSettings}}}""");
        return $$"""{"Rotation":{{rotation}},"Placements":[{{string.Join(",", items)}}]}""";
    }

    private void AssertFallsBackAndKeepsBackup(string json)
    {
        File.WriteAllText(FilePath, json);

        var layout = new LayoutStore(FilePath).Load();

        Assert.Equal(180, layout.Rotation);
        Assert.Same(DashboardLayout.Default, layout);
        Assert.Equal(json, File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void Load_Rotation90_FallsBackAndKeepsBackup() =>
        AssertFallsBackAndKeepsBackup(LayoutJson(90, (0, 0, 4, 2)));

    [Fact]
    public void Load_OverlappingPlacements_FallsBackAndKeepsBackup() =>
        AssertFallsBackAndKeepsBackup(LayoutJson(180, (0, 0, 4, 2), (3, 1, 4, 2)));

    [Theory]
    [InlineData(14, 0, 4, 2)]
    [InlineData(0, 3, 4, 2)]
    [InlineData(-1, 0, 4, 2)]
    [InlineData(0, 0, 0, 2)]
    public void Load_OutOfGrid_FallsBackAndKeepsBackup(int c, int r, int cs, int rs) =>
        AssertFallsBackAndKeepsBackup(LayoutJson(180, (c, r, cs, rs)));

    [Theory]
    [InlineData("""{"InstanceId":"11111111-1111-1111-1111-111111111111","Column":0,"Row":0,"Columns":4,"Rows":2,"TextSize":"Normal","Settings":{}}""")]
    [InlineData("""{"InstanceId":"11111111-1111-1111-1111-111111111111","TypeId":null,"Column":0,"Row":0,"Columns":4,"Rows":2,"TextSize":"Normal","Settings":{}}""")]
    [InlineData("""{"InstanceId":"11111111-1111-1111-1111-111111111111","TypeId":"  ","Column":0,"Row":0,"Columns":4,"Rows":2,"TextSize":"Normal","Settings":{}}""")]
    public void Load_MissingTypeId_FallsBackAndKeepsBackup(string placement) =>
        AssertFallsBackAndKeepsBackup("""{"Rotation":180,"Placements":[""" + placement + "]}");

    [Fact]
    public void Load_DuplicateInstanceId_FallsBackAndKeepsBackup()
    {
        string Place(int column) =>
            """{"InstanceId":"11111111-1111-1111-1111-111111111111","TypeId":"clock","Column":COL,"Row":0,"Columns":4,"Rows":2,"TextSize":"Normal","Settings":{}}"""
                .Replace("COL", column.ToString());

        AssertFallsBackAndKeepsBackup("""{"Rotation":180,"Placements":[""" + Place(0) + "," + Place(4) + "]}");
    }

    [Fact]
    public void Load_IntOverflowPlacement_FallsBackAndKeepsBackup() =>
        AssertFallsBackAndKeepsBackup(LayoutJson(180, (int.MaxValue, 0, 2, 2)));

    [Fact]
    public void Load_ValidHandWrittenFile_IsAccepted()
    {
        File.WriteAllText(FilePath, LayoutJson(0, (0, 0, 4, 2), (4, 0, 4, 2)));

        var layout = new LayoutStore(FilePath).Load();

        Assert.Equal(0, layout.Rotation);
        Assert.Equal(2, layout.Placements.Count);
        Assert.False(File.Exists(FilePath + ".bad"));
    }
}
