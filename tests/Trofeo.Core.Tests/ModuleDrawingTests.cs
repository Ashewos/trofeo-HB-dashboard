using SkiaSharp;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Tests;

public class ModuleDrawingTests
{
    private static SKFont NewFont(float size = 24f) => new(Themes.Classique.Typeface, size);

    [Fact]
    public void Ellipsize_ShortText_Unchanged()
    {
        using var font = NewFont();
        Assert.Equal("Bonjour", ModuleDrawing.Ellipsize("Bonjour", font, 500f));
    }

    [Fact]
    public void Ellipsize_LongText_FitsWidth()
    {
        using var font = NewFont();
        var text = new string('M', 200);
        var result = ModuleDrawing.Ellipsize(text, font, 150f);

        Assert.EndsWith("…", result);
        Assert.True(result.Length > 1);
        Assert.True(ModuleDrawing.MeasureText(result, font) <= 150f);
    }

    [Fact]
    public void Ellipsize_WidthTooSmallEvenForEllipsis_ReturnsEllipsis()
    {
        using var font = NewFont();
        Assert.Equal("…", ModuleDrawing.Ellipsize("Bonjour le monde", font, 1f));
    }

    [Fact]
    public void Ellipsize_NeverSplitsSurrogatePair()
    {
        using var font = NewFont();
        var text = string.Concat(Enumerable.Repeat("🎵", 50));
        var result = ModuleDrawing.Ellipsize(text, font, 100f);

        Assert.EndsWith("…", result);
        var body = result[..^1];
        Assert.Equal(0, body.Length % 2);
        Assert.All(body.EnumerateRunes(), r => Assert.Equal(0x1F3B5, r.Value));
        Assert.True(ModuleDrawing.MeasureText(result, font) <= 100f);
    }

    [Fact]
    public void Wrap_BreaksOnSpaces_AndRespectsMaxLines()
    {
        using var font = NewFont();
        var text = string.Join(' ', Enumerable.Repeat("mot", 60));
        var lines = ModuleDrawing.Wrap(text, font, 200f, 3);

        Assert.Equal(3, lines.Count);
        Assert.EndsWith("…", lines[2]);
        Assert.All(lines, l => Assert.True(ModuleDrawing.MeasureText(l, font) <= 200f));
        Assert.DoesNotContain("  ", lines[0]);
        // Les deux premières lignes sont coupées sur des espaces : mots entiers uniquement.
        Assert.All(lines.Take(2), l => Assert.All(l.Split(' '), w => Assert.Equal("mot", w)));
    }

    [Fact]
    public void Wrap_ShortText_SingleLine()
    {
        using var font = NewFont();
        var lines = ModuleDrawing.Wrap("Deux mots", font, 500f, 3);
        Assert.Equal(new[] { "Deux mots" }, lines);
    }

    [Fact]
    public void Wrap_WordLongerThanWidth_IsEllipsizedOnItsOwnLine()
    {
        using var font = NewFont();
        var lines = ModuleDrawing.Wrap("a " + new string('W', 100) + " b", font, 120f, 5);

        Assert.Equal(3, lines.Count);
        Assert.Equal("a", lines[0]);
        Assert.EndsWith("…", lines[1]);
        Assert.True(ModuleDrawing.MeasureText(lines[1], font) <= 120f);
        Assert.Equal("b", lines[2]);
    }

    [Fact]
    public void DrawText_MissingGlyphs_UsesFallback()
    {
        using var font = NewFont();
        // Garde-fou : Barlow ne contient pas ce caractère, sinon le test ne prouve rien.
        Assert.False(Themes.Classique.Typeface.ContainsGlyph('東'));

        // Sans repli, le .notdef de Barlow aurait sa propre largeur : une mesure différente prouve le repli.
        var withFallback = ModuleDrawing.MeasureText("東京", font);
        Assert.True(withFallback > 0f);
        Assert.NotEqual(font.MeasureText("東京"), withFallback);
        Assert.True(ModuleDrawing.MeasureText("東京 🎵", font) > withFallback);

        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var viaKit = RenderOnBlack(c => ModuleDrawing.DrawText(c, "東京 🎵", 10f, 50f, SKTextAlign.Left, font, paint));
        using var forcedPrimary = RenderOnBlack(c => c.DrawText("東京 🎵", 10f, 50f, SKTextAlign.Left, font, paint));

        Assert.True(CountPixels(viaKit, p => p != SKColors.Black) > 0);
        Assert.NotEqual(forcedPrimary.Bytes, viaKit.Bytes);
    }

    [Fact]
    public void Ellipsize_JapaneseAndEmojiTitle_FitsWidth()
    {
        using var font = NewFont();
        var title = string.Concat(Enumerable.Repeat("東京の夜 🎵 ", 20));
        var result = ModuleDrawing.Ellipsize(title, font, 180f);

        Assert.EndsWith("…", result);
        Assert.True(result.Length > 1);
        Assert.True(ModuleDrawing.MeasureText(result, font) <= 180f);
    }

    [Fact]
    public void Wrap_JapaneseAndEmojiTitle_KeepsEveryLineWithinWidth()
    {
        using var font = NewFont();
        var title = string.Join(' ', Enumerable.Repeat("東京の夜 🎵", 30));
        var lines = ModuleDrawing.Wrap(title, font, 180f, 3);

        Assert.Equal(3, lines.Count);
        Assert.EndsWith("…", lines[2]);
        Assert.All(lines, l => Assert.True(ModuleDrawing.MeasureText(l, font) <= 180f));

        var oneLongWord = ModuleDrawing.Wrap(new string('東', 100) + "🎵", font, 180f, 3);
        Assert.Single(oneLongWord);
        Assert.True(ModuleDrawing.MeasureText(oneLongWord[0], font) <= 180f);
    }

    [Fact]
    public void DrawText_RightAlign_EndsAtX()
    {
        using var font = NewFont();
        using var bitmap = new SKBitmap(300, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        ModuleDrawing.DrawText(canvas, "Droite", 200f, 50f, SKTextAlign.Right, font, paint);

        var width = ModuleDrawing.MeasureText("Droite", font);
        Assert.True(width > 0f);
        Assert.Equal(0, CountPixels(bitmap, p => p != SKColors.Black, xFrom: 205));
        Assert.True(CountPixels(bitmap, p => p != SKColors.Black, xFrom: (int)(200 - width) - 2, xTo: 200) > 0);
        Assert.Equal(0, CountPixels(bitmap, p => p != SKColors.Black, xTo: (int)(200 - width) - 4));
    }

    [Fact]
    public void DrawBar_NaN_DrawsTrackOnly()
    {
        using var bitmap = new SKBitmap(200, 40);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var fill = new SKColor(255, 0, 0);
        var track = new SKColor(0, 0, 255);

        ModuleDrawing.DrawBar(canvas, new SKRect(10, 10, 190, 30), float.NaN, fill, track);

        Assert.Equal(track, bitmap.GetPixel(100, 20));
        Assert.Equal(0, CountPixels(bitmap, p => p == fill));
    }

    [Fact]
    public void DrawBar_HalfFraction_FillsLeftHalfOnly()
    {
        using var bitmap = new SKBitmap(200, 40);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var fill = new SKColor(255, 0, 0);
        var track = new SKColor(0, 0, 255);

        ModuleDrawing.DrawBar(canvas, new SKRect(10, 10, 190, 30), 0.5f, fill, track);

        Assert.Equal(fill, bitmap.GetPixel(50, 20));
        Assert.Equal(track, bitmap.GetPixel(150, 20));
    }

    [Fact]
    public void DrawBar_OutOfRangeFraction_IsClamped()
    {
        using var bitmap = new SKBitmap(200, 40);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var fill = new SKColor(255, 0, 0);
        var track = new SKColor(0, 0, 255);

        ModuleDrawing.DrawBar(canvas, new SKRect(10, 10, 190, 30), 7f, fill, track);
        Assert.Equal(fill, bitmap.GetPixel(180, 20));

        canvas.Clear(SKColors.Black);
        ModuleDrawing.DrawBar(canvas, new SKRect(10, 10, 190, 30), -3f, fill, track);
        Assert.Equal(0, CountPixels(bitmap, p => p == fill));
        Assert.Equal(track, bitmap.GetPixel(100, 20));
    }

    [Fact]
    public void DrawActionMessage_LongMessage_StaysInsideArea()
    {
        var theme = Themes.Classique;
        using var bitmap = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var area = new SKRect(100, 60, 300, 140);
        var message = string.Join(' ', Enumerable.Repeat("Connexion impossible, ouvrez les réglages", 10));

        ModuleDrawing.DrawActionMessage(canvas, area, theme, 1f, message);

        var inside = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var painted = bitmap.GetPixel(x, y).Alpha != 0;
                var isInside = x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom;
                if (painted && isInside) inside++;
                Assert.True(!painted || isInside, $"pixel peint hors de area en ({x},{y})");
            }
        }
        Assert.True(inside > 0);
    }

    [Fact]
    public void DrawActionMessage_ShortMessage_IsCentered()
    {
        var theme = Themes.Classique;
        using var bitmap = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var area = new SKRect(0, 0, 400, 200);

        ModuleDrawing.DrawActionMessage(canvas, area, theme, 1f, "Réglages requis");

        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha != 0)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }

        Assert.True(maxX >= 0);
        Assert.InRange((minX + maxX) / 2.0, 190, 210);
        Assert.InRange((minY + maxY) / 2.0, 80, 120);
    }

    [Fact]
    public void LayoutActionMessage_ShortMessage_UsesFullSize()
    {
        var (size, lines) = ModuleDrawing.LayoutActionMessage(Themes.Classique, 1f, 300f, "Réglages requis");

        Assert.Equal(24f, size);
        Assert.Equal(new[] { "Réglages requis" }, lines);
    }

    [Fact]
    public void LayoutActionMessage_MessageNeedingMoreThanThreeLines_ShrinksInsteadOfEllipsizing()
    {
        var theme = Themes.Classique;
        var (n, width, size, lines) = FindMessage(theme, 1f, (sz, ls) => sz < 24f && !ls[^1].EndsWith('…'));

        Assert.True(n > 0, "aucun message ne demande une réduction sans ellipse");
        Assert.InRange(size, 16f, 22f);
        Assert.True(lines.Count <= 3);
        // À pleine taille, le même message aurait dû être ellipsé.
        using var full = new SKFont(theme.Typeface, 24f);
        var atFullSize = ModuleDrawing.Wrap(Message(n), full, width, 3);
        Assert.Equal(3, atFullSize.Count);
        Assert.EndsWith("…", atFullSize[2]);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.1f)]
    [InlineData(1.2f)]
    [InlineData(0.85f)]
    public void LayoutActionMessage_HugeMessage_StopsExactlyAtMinimumSize(float scale)
    {
        var (size, lines) = ModuleDrawing.LayoutActionMessage(Themes.Classique, scale, 300f, Message(500));

        Assert.Equal(16f * scale, size, 3);
        Assert.Equal(3, lines.Count);
        Assert.EndsWith("…", lines[2]);
    }

    [Fact]
    public void LayoutActionMessage_FractionalScale_StillTriesMinimumSize()
    {
        // Avec 1.1 : 26.4, 24.4, 22.4, 20.4, 18.4 puis 17.6 (minimum) - ce dernier doit être essayé.
        var (n, _, size, lines) = FindMessage(Themes.Classique, 1.1f, (sz, ls) => Math.Abs(sz - 17.6f) < 0.001f && !ls[^1].EndsWith('…'));

        Assert.True(n > 0, "aucun message ne tient exactement à la taille minimale 17.6");
        Assert.Equal(17.6f, size, 3);
        Assert.True(lines.Count <= 3);
    }

    private static string Message(int words) => string.Join(' ', Enumerable.Repeat("mot", words));

    /// <summary>Cherche (largeur, nombre de mots) dont la mise en page satisfait le prédicat ; <c>Words</c> = 0 si aucun.</summary>
    private static (int Words, float Width, float Size, IReadOnlyList<string> Lines) FindMessage(
        Theme theme, float scale, Func<float, IReadOnlyList<string>, bool> predicate)
    {
        for (var width = 200f; width <= 400f; width += 1f)
        {
            for (var n = 1; n <= 200; n++)
            {
                var (size, lines) = ModuleDrawing.LayoutActionMessage(theme, scale, width, Message(n));
                if (predicate(size, lines)) return (n, width, size, lines);
            }
        }
        return (0, 0f, 0f, Array.Empty<string>());
    }

    private static SKBitmap RenderOnBlack(Action<SKCanvas> draw)
    {
        var bitmap = new SKBitmap(300, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        draw(canvas);
        return bitmap;
    }

    private static int CountPixels(SKBitmap bitmap, Func<SKColor, bool> predicate, int xFrom = 0, int xTo = int.MaxValue)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = Math.Max(0, xFrom); x < Math.Min(bitmap.Width, xTo); x++)
                if (predicate(bitmap.GetPixel(x, y))) count++;
        return count;
    }
}
