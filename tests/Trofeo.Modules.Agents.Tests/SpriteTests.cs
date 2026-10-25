using SkiaSharp;

namespace Trofeo.Modules.Agents.Tests;

public sealed class SpriteTests
{
    private static readonly SKColor Red = new(200, 30, 30);

    private static Dictionary<char, SKColor> Palette() => new() { ['o'] = Red, ['x'] = new SKColor(0, 0, 255) };

    [Fact]
    public void Sprite_ParsesAndDraws()
    {
        var sprite = new Sprite(["o."], Palette());
        Assert.Equal(2, sprite.Width);
        Assert.Equal(1, sprite.Height);

        using var bitmap = new SKBitmap(8, 4);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            sprite.Draw(canvas, 0, 0, 4);
        }

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(Red, bitmap.GetPixel(x, y));
            }

            for (var x = 4; x < 8; x++)
            {
                Assert.Equal(0, bitmap.GetPixel(x, y).Alpha);
            }
        }
    }

    [Fact]
    public void Sprite_Draw_UsesOffsetRowsAndPaletteColors()
    {
        var sprite = new Sprite(["ox", ".o"], Palette());
        using var bitmap = new SKBitmap(10, 10);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            sprite.Draw(canvas, 2, 3, 2);
        }

        Assert.Equal(Red, bitmap.GetPixel(2, 3));
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(4, 3));
        Assert.Equal(0, bitmap.GetPixel(2, 5).Alpha);
        Assert.Equal(Red, bitmap.GetPixel(5, 6));
        Assert.Equal(0, bitmap.GetPixel(1, 3).Alpha);
        Assert.Equal(0, bitmap.GetPixel(6, 6).Alpha);
    }

    [Fact]
    public void Sprite_Draw_AlphaFadesTheImage()
    {
        var sprite = new Sprite(["o"], Palette());
        using var bitmap = new SKBitmap(2, 2);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            sprite.Draw(canvas, 0, 0, 2, alpha: 128);
        }

        Assert.InRange(bitmap.GetPixel(0, 0).Alpha, 126, 130);
    }

    [Fact]
    public void Sprite_UnknownChar_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sprite(["oz"], Palette()));
    }

    [Fact]
    public void Sprite_RaggedRows_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sprite(["oo", "o"], Palette()));
    }
}
