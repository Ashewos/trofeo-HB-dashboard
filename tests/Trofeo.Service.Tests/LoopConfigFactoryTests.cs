using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;

namespace Trofeo.Display.Tests;

public sealed class LoopConfigFactoryTests
{
    /// <summary>Aucune police installée.</summary>
    private sealed class NoSystemFonts : ISystemFonts
    {
        public IReadOnlyList<string> Families => [];
        public SKTypeface? Match(string family) => null;
    }

    private static DashboardLayout With(AppearanceSettings? appearance) =>
        LoopRig.Layout(LoopRig.Place(LoopRig.A)) with { Appearance = appearance };

    [Fact]
    public void Build_DefaultAppearance_IsClassiqueWithoutWarning()
    {
        var warnings = new List<string>();
        var layout = With(null);

        var config = LoopConfigFactory.Build(layout, new NoSystemFonts(), null, warnings.Add);
        using var background = config.Background;

        Assert.Same(layout, config.Layout);
        Assert.Same(Themes.ClassiqueDefinition, config.Theme);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_UnknownFont_WarnsAndKeepsTheTheme()
    {
        var warnings = new List<string>();

        var config = LoopConfigFactory.Build(
            With(new AppearanceSettings("pixel", Font: "PoliceIntrouvable")), new NoSystemFonts(), null, warnings.Add);
        using var background = config.Background;

        Assert.Contains(warnings, w => w.Contains("PoliceIntrouvable"));
        Assert.Equal("pixel", config.Theme.Theme.Id);
    }

    [Fact]
    public void Build_MissingImage_WarnsAndFallsBackToTheThemeBackground()
    {
        var warnings = new List<string>();
        var missing = Path.Combine(Path.GetTempPath(), "trofeo-absent", "fond.png");
        var appearance = new AppearanceSettings(Background: new BackgroundSettings(BackgroundKind.Image, Path: missing));

        var config = LoopConfigFactory.Build(With(appearance), new NoSystemFonts(), null, warnings.Add);
        using var background = config.Background;

        Assert.Contains(warnings, w => w.Contains("fond.png"));
        Assert.NotNull(config.Background);
    }

    [Fact]
    public void Build_Video_UsesTheOpenerAndGivesOwnershipToTheConfig()
    {
        var opened = new List<string>();
        var video = new TrackedBackground(SKColors.Red);
        var appearance = new AppearanceSettings(Background: new BackgroundSettings(BackgroundKind.Video, Path: "clip.mp4"));

        var config = LoopConfigFactory.Build(With(appearance), new NoSystemFonts(), path => { opened.Add(path); return video; }, _ => { });

        Assert.Equal(["clip.mp4"], opened);
        Assert.Same(video, config.Background);
        Assert.False(video.Disposed);
        config.Background.Dispose();
    }

    [Fact]
    public void Build_VideoWithoutOpener_WarnsNotSupported()
    {
        var warnings = new List<string>();
        var appearance = new AppearanceSettings(Background: new BackgroundSettings(BackgroundKind.Video, Path: "clip.mp4"));

        var config = LoopConfigFactory.Build(With(appearance), new NoSystemFonts(), null, warnings.Add);
        using var background = config.Background;

        Assert.Single(warnings);
    }

    [Fact]
    public void Build_SameBackgroundAndThemeDefault_KeepsTheCurrentBackground()
    {
        var opened = 0;
        Func<string, IBackground?> video = _ => { opened++; return new TrackedBackground(SKColors.Red); };
        var backgroundSettings = new BackgroundSettings(BackgroundKind.Video, Path: "clip.mp4");
        var current = LoopConfigFactory.Build(
            With(new AppearanceSettings(Background: backgroundSettings)), new NoSystemFonts(), video, _ => { });

        var next = LoopConfigFactory.Build(
            With(new AppearanceSettings(PanelOpacity: 0.4f, Font: "Inconnue", Legibility: false, Background: backgroundSettings)),
            new NoSystemFonts(), video, _ => { }, current);

        Assert.Same(current.Background, next.Background);
        Assert.Equal(1, opened);
        Assert.Equal(0.4f, next.Theme.Theme.PanelOpacity);
        Assert.False(next.Theme.LegibilityGuard);
        current.Background.Dispose();
    }

    [Fact]
    public void Build_ThemeWithAnotherDefaultBackground_RebuildsTheBackground()
    {
        var current = LoopConfigFactory.Build(With(new AppearanceSettings("classique")), new NoSystemFonts(), null, _ => { });

        var next = LoopConfigFactory.Build(With(new AppearanceSettings("pixel")), new NoSystemFonts(), null, _ => { }, current);

        Assert.NotSame(current.Background, next.Background);
        current.Background.Dispose();
        next.Background.Dispose();
    }

    [Fact]
    public void Build_BackgroundSettingsChanged_RebuildsTheBackground()
    {
        var current = LoopConfigFactory.Build(
            With(new AppearanceSettings(Background: new BackgroundSettings(BackgroundKind.Color, Color: "#102030"))),
            new NoSystemFonts(), null, _ => { });

        var next = LoopConfigFactory.Build(
            With(new AppearanceSettings(Background: new BackgroundSettings(BackgroundKind.Color, Color: "#405060"))),
            new NoSystemFonts(), null, _ => { }, current);

        Assert.NotSame(current.Background, next.Background);
        current.Background.Dispose();
        next.Background.Dispose();
    }

    [Fact]
    public void Build_KeptBackground_RepeatsItsWarnings()
    {
        var missing = Path.Combine(Path.GetTempPath(), "trofeo-absent", "fond.png");
        var backgroundSettings = new BackgroundSettings(BackgroundKind.Image, Path: missing);
        var first = new List<string>();
        var current = LoopConfigFactory.Build(
            With(new AppearanceSettings(Background: backgroundSettings)), new NoSystemFonts(), null, first.Add);
        var second = new List<string>();

        var next = LoopConfigFactory.Build(
            With(new AppearanceSettings(PanelOpacity: 0.5f, Background: backgroundSettings)), new NoSystemFonts(), null, second.Add, current);

        Assert.Same(current.Background, next.Background);
        Assert.NotEmpty(first);
        Assert.Equal(first, second); // la fenêtre remplace ses avertissements d'apparence : le fond de repli reste signalé
        current.Background.Dispose();
    }
}
