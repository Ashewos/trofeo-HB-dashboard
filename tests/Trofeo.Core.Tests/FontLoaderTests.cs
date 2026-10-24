using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public class FontLoaderTests
{
    [Theory]
    [InlineData("BarlowSemiCondensed-Regular.ttf")]
    [InlineData("BarlowSemiCondensed-SemiBold.ttf")]
    public void Load_EmbeddedFont_ReturnsBarlow(string resourceName)
    {
        using var typeface = FontLoader.Load(resourceName);

        Assert.StartsWith("Barlow", typeface.FamilyName);
        Assert.Equal((int)SKFontStyleWidth.SemiCondensed, typeface.FontWidth);
    }

    [Fact]
    public void Load_UnknownResource_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => FontLoader.Load("absente.ttf"));
    }

    [Fact]
    public void Classique_UsesBarlowTypeface()
    {
        Assert.StartsWith("Barlow", Themes.Classique.Typeface.FamilyName);
        Assert.Equal((int)SKFontStyleWidth.SemiCondensed, Themes.Classique.Typeface.FontWidth);
    }
}
