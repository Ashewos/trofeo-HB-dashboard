using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public sealed class AppearanceTests
{
    /// <summary>Aucune police installée.</summary>
    private sealed class NoSystemFonts : ISystemFonts
    {
        public IReadOnlyList<string> Families => Array.Empty<string>();
        public SKTypeface? Match(string family) => null;
    }

    private static ThemeDefinition Resolve(AppearanceSettings settings, List<string>? warnings = null) =>
        AppearanceResolver.Resolve(settings, new NoSystemFonts(), warnings is null ? null : warnings.Add);

    [Fact]
    public void Resolve_Default_IsClassique()
    {
        var warnings = new List<string>();

        var def = Resolve(AppearanceSettings.Default, warnings);

        Assert.Same(Themes.ClassiqueDefinition, def);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Resolve_KnownTheme_IsThatTheme()
    {
        Assert.Same(Themes.Pixel, Resolve(new AppearanceSettings("pixel")));
        Assert.Same(Themes.LiquidGlass, Resolve(new AppearanceSettings("Liquid-Glass")));
    }

    [Fact]
    public void Resolve_UnknownTheme_IsClassiqueWithWarning()
    {
        var warnings = new List<string>();

        var def = Resolve(new AppearanceSettings("neon"), warnings);

        Assert.Same(Themes.ClassiqueDefinition, def);
        Assert.Equal("Thème « neon » inconnu : thème Classique utilisé.", Assert.Single(warnings));
    }

    [Fact]
    public void Resolve_Opacity_ReplacesThemeOpacity()
    {
        var def = Resolve(new AppearanceSettings(PanelOpacity: 0.4f));

        Assert.Equal(0.4f, def.Theme.PanelOpacity);
        Assert.Equal(Themes.ClassiqueDefinition.Panel, def.Panel);
        Assert.Equal(Themes.ClassiqueDefinition.Theme.Panel, def.Theme.Panel);
    }

    [Fact]
    public void Resolve_Opacity_IsClamped()
    {
        Assert.Equal(1f, Resolve(new AppearanceSettings(PanelOpacity: 3f)).Theme.PanelOpacity);
        Assert.Equal(0f, Resolve(new AppearanceSettings(PanelOpacity: -1f)).Theme.PanelOpacity);
    }

    [Fact]
    public void Resolve_NaNOpacity_KeepsThemeOpacityWithWarning()
    {
        var warnings = new List<string>();

        var def = Resolve(new AppearanceSettings(PanelOpacity: float.NaN), warnings);

        Assert.Equal(Themes.ClassiqueDefinition.Theme.PanelOpacity, def.Theme.PanelOpacity);
        Assert.Equal("Opacité des modules invalide : thème par défaut utilisé.", Assert.Single(warnings));
    }

    [Fact]
    public void Resolve_Font_ReplacesBothTypefaces()
    {
        var manrope = EmbeddedFonts.Find("Manrope")!;

        var def = Resolve(new AppearanceSettings("pixel", Font: "Manrope"));

        Assert.Same(manrope, def.Theme.Typeface);
        Assert.Same(manrope, def.Theme.FeedTypeface);
        Assert.Equal(1f, def.Theme.FeedScale);
        Assert.Equal("pixel", def.Theme.Id);
    }

    [Fact]
    public void Resolve_FontEqualToThemeFont_StillReplacesFeedTypeface()
    {
        var def = Resolve(new AppearanceSettings("pixel", Font: "Pixelify Sans"));

        Assert.Equal(1f, def.Theme.FeedScale);
        Assert.Same(def.Theme.Typeface, def.Theme.FeedTypeface);
        Assert.Equal("Pixelify Sans", def.Theme.FeedTypeface.FamilyName);
    }

    [Fact]
    public void Resolve_MissingFont_KeepsThemeUntouchedWithWarning()
    {
        var warnings = new List<string>();

        var def = Resolve(new AppearanceSettings("pixel", Font: "Inexistante"), warnings);

        Assert.Same(Themes.Pixel, def);
        Assert.Equal("Police « Inexistante » introuvable : police du thème utilisée.", Assert.Single(warnings));
    }

    [Fact]
    public void Resolve_FontAndOpacity_BothApply()
    {
        var def = Resolve(new AppearanceSettings("liquid-glass", 0.5f, "Pixelify Sans"));

        Assert.Equal(0.5f, def.Theme.PanelOpacity);
        Assert.Same(EmbeddedFonts.Find("Pixelify Sans"), def.Theme.Typeface);
    }
}
