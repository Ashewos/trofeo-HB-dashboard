using SkiaSharp;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Tests;

public class ThemeTests
{
    [Fact]
    public void Theme_Defaults_FollowTextColors()
    {
        var classique = Themes.Classique;
        var theme = new Theme(
            classique.Id, classique.Background, classique.Panel, classique.PanelOpacity, classique.CornerRadius,
            classique.Text, classique.TextMuted, classique.Accent, classique.Warning, classique.Typeface);

        Assert.Equal(ThemeShape.Rounded, theme.Shape);
        Assert.Equal(theme.Text, theme.Highlight);
        Assert.Equal(theme.Text.WithAlpha(18), theme.Surface);
        Assert.Equal(theme.Text.WithAlpha(20), theme.Track);
        Assert.Same(theme.Typeface, theme.FeedTypeface);
        Assert.Equal(1f, theme.FeedScale);
    }

    [Fact]
    public void Theme_TypefaceScale_DefaultsToOne()
    {
        var classique = Themes.Classique;
        var theme = new Theme(
            classique.Id, classique.Background, classique.Panel, classique.PanelOpacity, classique.CornerRadius,
            classique.Text, classique.TextMuted, classique.Accent, classique.Warning, classique.Typeface);

        Assert.Equal(1f, theme.TypefaceScale);
        Assert.Equal(1f, Themes.Classique.TypefaceScale);
        Assert.Equal(1f, Themes.LiquidGlass.Theme.TypefaceScale);
        Assert.Equal(1.3f, (theme with { TypefaceScale = 1.3f } with { Text = new SKColor(1, 2, 3) }).TypefaceScale);
    }

    [Fact]
    public void Theme_Classique_KeepsDefaults()
    {
        var theme = Themes.Classique;

        Assert.Equal(ThemeShape.Rounded, theme.Shape);
        Assert.Equal(theme.Text, theme.Highlight);
        Assert.Equal(theme.Text.WithAlpha(18), theme.Surface);
        Assert.Equal(theme.Text.WithAlpha(20), theme.Track);
        Assert.Same(theme.Typeface, theme.FeedTypeface);
        Assert.Equal(1f, theme.FeedScale);
    }

    [Fact]
    public void Theme_With_UnsetDefaultsFollowNewText()
    {
        var red = new SKColor(255, 0, 0);
        var changed = Themes.Classique with { Text = red };

        Assert.Equal(red, changed.Highlight);
        Assert.Equal(red.WithAlpha(18), changed.Surface);
        Assert.Equal(red.WithAlpha(20), changed.Track);
    }

    [Fact]
    public void Theme_With_ExplicitValuesAreKept()
    {
        var green = new SKColor(0, 255, 0);
        var theme = Themes.Classique with { Highlight = green, Shape = ThemeShape.Pixel, FeedScale = 1.5f };
        var changed = theme with { Text = new SKColor(255, 0, 0) };

        Assert.Equal(green, changed.Highlight);
        Assert.Equal(ThemeShape.Pixel, changed.Shape);
        Assert.Equal(1.5f, changed.FeedScale);
    }
}
