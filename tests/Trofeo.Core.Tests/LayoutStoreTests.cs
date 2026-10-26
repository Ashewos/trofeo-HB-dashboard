using System.Text.Json;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Core.Shortcuts;
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
    public void Default_HasFiveModules_NoOverlap()
    {
        var layout = DashboardLayout.Default;

        Assert.Equal(180, layout.Rotation);
        Assert.Equal(
            [("claudecode", 0, 0), ("monitoring", 8, 0), ("nowplaying", 8, 2), ("clock", 12, 0), ("network", 12, 2)],
            layout.Placements.Select(p => (p.TypeId, p.Column, p.Row)));
        Assert.All(layout.Placements.Where(p => p.TypeId != "claudecode"), p => Assert.Equal((4, 2), (p.Columns, p.Rows)));
        var claude = layout.Placements.Single(p => p.TypeId == "claudecode");
        Assert.Equal((8, 4), (claude.Columns, claude.Rows));
        Assert.Equal(new Guid("3c1d9f5e-7a2b-4e8c-9b1f-6d2a0e4c8b17"), claude.InstanceId);
        Assert.Equal("feed", claude.Settings.GetProperty("view").GetString());
        Assert.True(claude.Settings.GetProperty("autoFollow").GetBoolean());
        Assert.Null(layout.Shortcuts);
        Assert.Equal(layout.Placements.Count, layout.Placements.Select(p => p.InstanceId).Distinct().Count());
        Assert.Equal(new Guid("5b0c5e2e-6a53-4c0b-9d0e-3f1a7c1d2b01"), layout.Placements.Single(p => p.TypeId == "clock").InstanceId);
        Assert.Equal("pc", layout.Placements.Single(p => p.TypeId == "network").Settings.GetProperty("source").GetString());
        // Les modules de données restent dans les colonnes 8 à 15 ; Claude Code occupe les colonnes 0 à 7.
        Assert.All(layout.Placements.Where(p => p.TypeId != "claudecode"), p => Assert.True(p.Column >= 8));
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
    public void Layout_WithShortcuts_RoundTrips()
    {
        var target = Guid.NewGuid();
        var placement = new ModulePlacement(
            Guid.NewGuid(), "clock", 0, 0, 4, 2, TextSize.Normal, JsonDocument.Parse("{}").RootElement.Clone());
        var layout = new DashboardLayout(180, [placement],
        [
            new ShortcutBinding("Ctrl+Alt+Right", ModuleCommandKind.NextTab),
            new ShortcutBinding("Ctrl+Shift+F5", ModuleCommandKind.SelectTab, 4, target),
        ]);
        var store = new LayoutStore(FilePath);

        store.Save(layout);
        var loaded = store.Load();

        Assert.NotNull(loaded.Shortcuts);
        Assert.Equal(
            [
                new ShortcutBinding("Ctrl+Alt+Right", ModuleCommandKind.NextTab),
                new ShortcutBinding("Ctrl+Shift+F5", ModuleCommandKind.SelectTab, 4, target),
            ],
            loaded.Shortcuts);
        Assert.Contains("\"NextTab\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Layout_OldFile_ShortcutsNull()
    {
        File.WriteAllText(FilePath, $$"""
            {"Rotation":180,"Placements":[{"InstanceId":"11111111-1111-1111-1111-111111111111","TypeId":"clock","Column":0,"Row":0,"Columns":4,"Rows":2,"TextSize":"Normal","Settings":{{ClockSettings}}}]}
            """);

        var loaded = new LayoutStore(FilePath).Load();

        Assert.Single(loaded.Placements);
        Assert.Null(loaded.Shortcuts);
    }

    [Fact]
    public void Layout_NullShortcutEntry_FallsBackToDefault()
    {
        File.WriteAllText(FilePath, """{"Rotation":180,"Placements":[],"Shortcuts":[null]}""");

        var loaded = new LayoutStore(FilePath).Load();

        Assert.Same(DashboardLayout.Default, loaded);
        Assert.True(File.Exists(FilePath + ".bad"));
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

    private static string WithAppearance(string layoutJson, string appearanceJson) =>
        layoutJson.TrimEnd().TrimEnd('}') + ",\"Appearance\":" + appearanceJson + "}";

    [Fact]
    public void Load_WithoutAppearance_IsNull()
    {
        File.WriteAllText(FilePath, LayoutJson(180, (0, 0, 4, 2)));

        var layout = new LayoutStore(FilePath).Load();

        Assert.Null(layout.Appearance);
        Assert.Single(layout.Placements);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void SaveLoad_WithoutAppearance_StaysNull()
    {
        var store = new LayoutStore(FilePath);

        store.Save(DashboardLayout.Default);

        Assert.Null(store.Load().Appearance);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void SaveLoad_Legibility_RoundTrips(bool? legibility)
    {
        var store = new LayoutStore(FilePath);

        store.Save(DashboardLayout.Default with { Appearance = new AppearanceSettings("pixel", Legibility: legibility) });

        Assert.Equal(legibility, store.Load().Appearance!.Legibility);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_AppearanceWithoutLegibility_IsNullAndValid()
    {
        var warnings = new List<string>();
        File.WriteAllText(FilePath, WithAppearance(LayoutJson(180, (0, 0, 4, 2)), """{"Theme":"pixel","PanelOpacity":0.4}"""));

        var layout = new LayoutStore(FilePath, warnings.Add).Load();

        Assert.Null(layout.Appearance!.Legibility);
        Assert.Equal(0.4f, layout.Appearance.PanelOpacity);
        Assert.Empty(warnings);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_AppearanceWithLegibilityFalse_IsRead()
    {
        File.WriteAllText(FilePath, WithAppearance(LayoutJson(180, (0, 0, 4, 2)), """{"Theme":"pixel","Legibility":false}"""));

        Assert.False(new LayoutStore(FilePath).Load().Appearance!.Legibility);
    }

    [Fact]
    public void Load_UnknownTheme_KeepsPlacements()
    {
        File.WriteAllText(FilePath, WithAppearance(LayoutJson(180, (0, 0, 4, 2)), """{"Theme":"neon"}"""));

        var layout = new LayoutStore(FilePath).Load();

        Assert.Single(layout.Placements);
        Assert.Equal("neon", layout.Appearance!.Theme);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"pixel\"")]
    [InlineData("[]")]
    [InlineData("""{"Theme":"pixel","Background":{"Kind":"Plasma"}}""")]
    [InlineData("""{"Theme":"pixel","Background":{"Kind":99}}""")]
    [InlineData("""{"Theme":42}""")]
    [InlineData("""{"Theme":null}""")]
    [InlineData("""{"Theme":"pixel","PanelOpacity":"beaucoup"}""")]
    [InlineData("""{"Theme":"pixel","Font":7}""")]
    public void Load_BrokenAppearance_KeepsLayout(string appearance)
    {
        File.WriteAllText(FilePath, WithAppearance(LayoutJson(180, (0, 0, 4, 2)), appearance));

        var warnings = new List<string>();
        var layout = new LayoutStore(FilePath, warnings.Add).Load();

        Assert.Equal(180, layout.Rotation);
        Assert.Equal("clock", Assert.Single(layout.Placements).TypeId);
        Assert.Null(layout.Appearance);
        Assert.False(File.Exists(FilePath + ".bad"));
        Assert.Equal(["Réglages d'apparence illisibles dans layout.json : apparence par défaut utilisée."], warnings);
    }

    [Fact]
    public void Load_ValidOrNullAppearance_DoesNotWarn()
    {
        var warnings = new List<string>();
        File.WriteAllText(FilePath, WithAppearance(LayoutJson(180, (0, 0, 4, 2)), """{"Theme":"pixel"}"""));
        Assert.Equal("pixel", new LayoutStore(FilePath, warnings.Add).Load().Appearance!.Theme);

        File.WriteAllText(FilePath, WithAppearance(LayoutJson(180, (0, 0, 4, 2)), "null"));
        Assert.Null(new LayoutStore(FilePath, warnings.Add).Load().Appearance);

        File.WriteAllText(FilePath, LayoutJson(180, (0, 0, 4, 2)));
        Assert.Null(new LayoutStore(FilePath, warnings.Add).Load().Appearance);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Load_DuplicateKeys_StayReadableLastValueWins()
    {
        // Avant le plan 4, le sérialiseur acceptait un nom de propriété en double (la dernière valeur gagne).
        var json = LayoutJson(180, (0, 0, 4, 2)).TrimEnd().TrimEnd('}') + ",\"Rotation\":0,\"Appearance\":{\"Theme\":\"classique\"}}";
        File.WriteAllText(FilePath, json);

        var layout = new LayoutStore(FilePath).Load();

        Assert.Equal(0, layout.Rotation);
        Assert.Equal("clock", Assert.Single(layout.Placements).TypeId);
        Assert.Equal("classique", layout.Appearance!.Theme);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_DuplicateAppearanceKey_StaysReadable()
    {
        var json = LayoutJson(180, (0, 0, 4, 2)).TrimEnd().TrimEnd('}') +
                   ",\"Appearance\":{\"Theme\":\"pixel\"},\"Appearance\":{\"Theme\":\"liquid-glass\"}}";
        File.WriteAllText(FilePath, json);

        var layout = new LayoutStore(FilePath).Load();

        Assert.Single(layout.Placements);
        Assert.Equal("liquid-glass", layout.Appearance!.Theme);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Load_InvalidPlacementsWithBrokenAppearance_StillFallsBack()
    {
        AssertFallsBackAndKeepsBackup(WithAppearance(LayoutJson(180, (int.MaxValue, 0, 2, 2)), "42"));
    }

    [Fact]
    public void SaveLoad_RoundTrips_Appearance()
    {
        var appearance = new AppearanceSettings(
            "liquid-glass", 0.5f, "Manrope",
            new BackgroundSettings(BackgroundKind.Animated, null, @"C:\Users\alex\Images\pluie.gif"));
        var layout = DashboardLayout.Default with { Appearance = appearance };
        var store = new LayoutStore(FilePath);

        store.Save(layout);
        var loaded = store.Load();

        Assert.Equal(appearance, loaded.Appearance);
        Assert.Equal(layout.Placements.Select(p => p.InstanceId), loaded.Placements.Select(p => p.InstanceId));
        var text = File.ReadAllText(FilePath);
        Assert.Contains("\"Kind\": \"Animated\"", text);
        Assert.Contains("\"Theme\": \"liquid-glass\"", text);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void SaveLoad_RoundTrips_DefaultAppearance()
    {
        var layout = DashboardLayout.Default with { Appearance = AppearanceSettings.Default };
        var store = new LayoutStore(FilePath);

        store.Save(layout);

        Assert.Equal(AppearanceSettings.Default, store.Load().Appearance);
    }
}
