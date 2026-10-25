using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

/// <summary>Fond de test : une image 1920 × 462 que le test remplace à volonté (la version monte à chaque remplacement).</summary>
internal sealed class StubBackground : IBackground
{
    private SKImage _image;

    public StubBackground(SKImage image) => _image = image;

    public StubBackground(SKColor color, SKColor? right = null) : this(Make(canvas =>
    {
        canvas.Clear(color);
        if (right is { } r)
        {
            using var paint = new SKPaint { Color = r };
            canvas.DrawRect(GridGeometry.Width / 2f, 0, GridGeometry.Width / 2f, GridGeometry.Height, paint);
        }
    }))
    {
    }

    public static SKImage Make(Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(GridGeometry.Width, GridGeometry.Height);
        using (var canvas = new SKCanvas(bitmap)) draw(canvas);
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    public bool IsAnimated => true;
    public SKImage Current => _image;
    public int Version { get; private set; } = 1;
    public TimeSpan? UntilNextFrame => TimeSpan.FromMilliseconds(100);

    public bool Advance(TimeSpan elapsed) => false;

    /// <summary>Remplace l'image : nouvelle version, l'ancienne image n'est plus valable.</summary>
    public void Replace(SKImage image)
    {
        _image.Dispose();
        _image = image;
        Version++;
    }

    /// <summary>Remplace l'image sans changer la version (pour vérifier qu'une valeur est gardée).</summary>
    public void ReplaceSilently(SKImage image)
    {
        _image.Dispose();
        _image = image;
    }

    public void Dispose() => _image.Dispose();
}

public class PanelPainterTests
{
    private static SKColor PixelAt(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    private static (SKBitmap Bitmap, SKCanvas Canvas) Surface(SKColor fill)
    {
        var bitmap = new SKBitmap(GridGeometry.Width, GridGeometry.Height);
        var canvas = new SKCanvas(bitmap);
        canvas.Clear(fill);
        return (bitmap, canvas);
    }

    // ---- Luminance et garde ----

    [Fact]
    public void RelativeLuminance_FollowsWcag()
    {
        Assert.Equal(1f, PanelPainter.RelativeLuminance(SKColors.White), 4);
        Assert.Equal(0f, PanelPainter.RelativeLuminance(SKColors.Black), 4);
        Assert.Equal(0.2159f, PanelPainter.RelativeLuminance(new SKColor(128, 128, 128)), 3);
        Assert.Equal(0.2126f, PanelPainter.RelativeLuminance(new SKColor(255, 0, 0)), 3);
    }

    [Fact]
    public void LegibleAlpha_BrightBackdrop_RaisesToExactlyTheLimit()
    {
        float lPanel = PanelPainter.RelativeLuminance(SKColor.Parse("#111317"));

        float a = PanelPainter.LegibleAlpha(1f, lPanel, 0f, 0f);

        Assert.InRange(a, 0.6f, 0.7f);
        Assert.Equal(0.35f, 1f * (1 - a) + lPanel * a, 4);
    }

    [Fact]
    public void LegibleAlpha_DarkBackdrop_KeepsUserOpacity()
    {
        Assert.Equal(0.3f, PanelPainter.LegibleAlpha(0.006f, 0.0065f, 0.3f, 0f));
        Assert.Equal(0.22f, PanelPainter.LegibleAlpha(0.006f, 0.0065f, 0.22f, 0.05f));
    }

    [Fact]
    public void LegibleAlpha_Sheen_CountsInTheLuminance()
    {
        // 0,32 reste sous la limite seul, mais pas avec les + 0,05 du reflet du verre.
        Assert.Equal(0f, PanelPainter.LegibleAlpha(0.32f, 0f, 0f, 0f));
        float a = PanelPainter.LegibleAlpha(0.32f, 0f, 0f, 0.05f);
        Assert.InRange(a, 0.05f, 0.2f);
        Assert.Equal(0.35f, 0.32f * (1 - a) + 0.05f, 4);
    }

    [Fact]
    public void LegibleAlpha_UserOpacityAboveTheNeed_IsKept()
    {
        Assert.Equal(0.9f, PanelPainter.LegibleAlpha(1f, 0.0065f, 0.9f, 0f));
    }

    [Fact]
    public void LegibleAlpha_PanelBrighterThanBackdrop_CannotHelp()
    {
        Assert.Equal(0.4f, PanelPainter.LegibleAlpha(0.2f, 0.6f, 0.4f, 0f));
    }

    // ---- Dégradé CSS ----

    [Fact]
    public void CssGradientLine_155Degrees_FollowsCssLengthRule()
    {
        var (start, end) = PanelPainter.CssGradientLine(155, SKRect.Create(10, 20, 100, 50));

        double expectedLength = 100 * Math.Sin(155 * Math.PI / 180) + 50 * Math.Abs(Math.Cos(155 * Math.PI / 180));
        Assert.Equal(expectedLength, Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)), 2);
        Assert.Equal(60f, (start.X + end.X) / 2f, 3);   // passe par le centre
        Assert.Equal(45f, (start.Y + end.Y) / 2f, 3);
        Assert.True(end.X > start.X && end.Y > start.Y); // vers le bas à droite
    }

    // ---- Mate ----

    [Fact]
    public void Paint_Matte_DrawsOneInsidePixelOutline()
    {
        var def = Themes.ClassiqueDefinition;
        var (bitmap, canvas) = Surface(def.Theme.Background);
        using var _ = bitmap; using var __ = canvas;
        using var background = new StubBackground(def.Theme.Background);
        var rect = SKRect.Create(200, 100, 300, 200);

        PanelPainter.Paint(canvas, rect, def, null, background.Current);

        var edge = PixelAt(bitmap, 200, 200);
        var inside = PixelAt(bitmap, 203, 200);
        var outside = PixelAt(bitmap, 199, 200);
        Assert.Equal(def.Theme.Background, outside);
        Assert.True(edge.Red > inside.Red + 5, $"contour attendu : bord {edge}, intérieur {inside}");
        Assert.Equal(inside, PixelAt(bitmap, 205, 200));
    }

    [Fact]
    public void Paint_NoLuminanceGiven_MeasuresTheBackgroundItself()
    {
        var def = Themes.ClassiqueDefinition with { Theme = Themes.Classique with { PanelOpacity = 0f } };
        var (bitmap, canvas) = Surface(SKColors.White);
        using var _ = bitmap; using var __ = canvas;
        using var background = new StubBackground(SKColors.White);

        PanelPainter.Paint(canvas, SKRect.Create(200, 100, 300, 200), def, null, background.Current);

        Assert.True(PixelAt(bitmap, 350, 200).Red < 200, "le panneau transparent devrait être assombri sur du blanc");
    }

    // ---- Verre ----

    [Fact]
    public void Paint_Glass_TopLightHugsTheTopEdgeOnly()
    {
        var def = Themes.LiquidGlass;
        var (bitmap, canvas) = Surface(new SKColor(20, 20, 20));
        using var _ = bitmap; using var __ = canvas;
        using var background = new StubBackground(new SKColor(20, 20, 20));
        using var backdrop = new Backdrop();
        var rect = SKRect.Create(400, 100, 400, 200);

        PanelPainter.Paint(canvas, rect, def, backdrop.Get(background, def.Panel), background.Current);

        // Sous la ligne du contour (1 px) : y = 101 est à moitié couvert par le trait de 1,5 px ; x = 401 de même côté.
        int topLight = PixelAt(bitmap, 600, 101).Red - PixelAt(bitmap, 600, 108).Red;
        int sideLight = Math.Abs(PixelAt(bitmap, 401, 200).Red - PixelAt(bitmap, 408, 200).Red);
        Assert.True(topLight > 30, $"trait du haut trop faible : {topLight}");
        Assert.True(sideLight < 10, $"le trait ne doit pas descendre le long du côté : {sideLight}");
    }

    [Fact]
    public void Paint_Glass_DropShadowFallsBelowAndNotInside()
    {
        var def = Themes.LiquidGlass with { Panel = Themes.LiquidGlass.Panel with { DropShadow = new SKColor(0, 0, 0, 200) } };
        var grey = new SKColor(150, 150, 150);
        var (bitmap, canvas) = Surface(grey);
        using var _ = bitmap; using var __ = canvas;
        using var background = new StubBackground(grey);
        using var backdrop = new Backdrop();
        var rect = SKRect.Create(400, 100, 400, 200);

        PanelPainter.Paint(canvas, rect, def, backdrop.Get(background, def.Panel), background.Current);

        Assert.True(PixelAt(bitmap, 600, 305).Red < grey.Red - 20, "l'ombre doit assombrir sous le panneau");
        Assert.Equal(grey, PixelAt(bitmap, 600, 40)); // loin au-dessus : intact
    }

    // ---- Pixel ----

    [Fact]
    public void Paint_Pixel_IsNotAntialiased()
    {
        var def = Themes.Pixel;
        var bg = new SKColor(112, 112, 112);
        var (bitmap, canvas) = Surface(bg);
        using var _ = bitmap; using var __ = canvas;
        using var background = new StubBackground(bg);

        PanelPainter.Paint(canvas, SKRect.Create(300, 100, 300, 200), def, null, background.Current);

        var colors = new HashSet<SKColor>();
        for (int y = 80; y < 330; y++)
            for (int x = 280; x < 630; x++)
                colors.Add(PixelAt(bitmap, x, y));
        // fond, ombre sur fond, bordure, panneau : rien d'autre (pas de pixel à demi-teinte)
        Assert.True(colors.Count <= 4, $"{colors.Count} couleurs : {string.Join(", ", colors)}");
    }

    [Fact]
    public void Paint_Pixel_BorderAndShadowStayOutsideThePanel()
    {
        var def = Themes.Pixel with { Theme = Themes.Pixel.Theme with { PanelOpacity = 0.5f } };
        var bg = new SKColor(112, 112, 112);
        var (bitmap, canvas) = Surface(bg);
        using var _ = bitmap; using var __ = canvas;
        using var background = new StubBackground(bg);

        PanelPainter.Paint(canvas, SKRect.Create(300, 100, 300, 200), def, null, background.Current);

        // Panneau à moitié transparent : on y voit le fond, pas la bordure ni l'ombre posées dessous.
        var inside = PixelAt(bitmap, 305, 105);
        var expected = new SKColor(
            (byte)Math.Round((def.Theme.Panel.Red + bg.Red) / 2.0),
            (byte)Math.Round((def.Theme.Panel.Green + bg.Green) / 2.0),
            (byte)Math.Round((def.Theme.Panel.Blue + bg.Blue) / 2.0));
        Assert.InRange(inside.Red, expected.Red - 2, expected.Red + 2);
        Assert.InRange(inside.Green, expected.Green - 2, expected.Green + 2);
        Assert.InRange(inside.Blue, expected.Blue - 2, expected.Blue + 2);
    }
}

public class BackdropTests
{
    private static SKImage Halves(SKColor left, SKColor right) => StubBackground.Make(canvas =>
    {
        canvas.Clear(left);
        using var paint = new SKPaint { Color = right };
        canvas.DrawRect(GridGeometry.Width / 2f, 0, GridGeometry.Width / 2f, GridGeometry.Height, paint);
    });

    [Fact]
    public void Get_IsQuarterSize()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(SKColors.Gray);

        var image = backdrop.Get(background, Themes.LiquidGlass.Panel);

        Assert.Equal(480, image.Width);
        Assert.Equal(116, image.Height);
    }

    [Fact]
    public void Get_SameBackgroundVersionAndLook_ComputesOnce()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(SKColors.Gray);
        var look = Themes.LiquidGlass.Panel;

        var first = backdrop.Get(background, look);
        var second = backdrop.Get(background, look with { });

        Assert.Same(first, second);
        Assert.Equal(1, backdrop.Computations);
    }

    [Fact]
    public void Get_NewVersion_Recomputes()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(SKColors.Gray);
        var look = Themes.LiquidGlass.Panel;
        backdrop.Get(background, look);

        background.Replace(StubBackground.Make(c => c.Clear(SKColors.Red)));
        backdrop.Get(background, look);

        Assert.Equal(2, backdrop.Computations);
    }

    [Fact]
    public void Get_OtherInstanceWithSameVersion_Recomputes()
    {
        using var backdrop = new Backdrop();
        using var a = new StubBackground(SKColors.Gray);
        using var b = new StubBackground(SKColors.Red);
        var look = Themes.LiquidGlass.Panel;

        backdrop.Get(a, look);
        backdrop.Get(b, look);

        Assert.Equal(2, backdrop.Computations);
    }

    [Fact]
    public void Get_OtherLook_Recomputes()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(SKColors.Gray);

        backdrop.Get(background, Themes.LiquidGlass.Panel);
        backdrop.Get(background, Themes.LiquidGlass.Panel with { BlurSigma = 10f });

        Assert.Equal(2, backdrop.Computations);
    }

    [Fact]
    public void Get_DoesNotKeepTheBackgroundImage()
    {
        // L'image courante d'un fond animé n'est plus valable après Advance : le flou vit dans sa propre surface.
        using var backdrop = new Backdrop();
        using var background = new StubBackground(SKColors.Red);
        var look = Themes.LiquidGlass.Panel;
        var blurred = backdrop.Get(background, look);

        background.Replace(StubBackground.Make(c => c.Clear(SKColors.Blue))); // libère l'ancienne image

        using var bitmap = SKBitmap.FromImage(blurred);
        var pixel = bitmap.GetPixel(240, 58);
        Assert.True(pixel.Red > 200 && pixel.Blue < 40, $"le flou doit rester rouge : {pixel}");
    }

    [Fact]
    public void Get_BlursAcrossTheBoundary_AndExtendsTheEdges()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(Halves(SKColors.Red, SKColors.Blue));

        using var bitmap = SKBitmap.FromImage(backdrop.Get(background, Themes.LiquidGlass.Panel));

        var middle = bitmap.GetPixel(240, 58);
        Assert.True(middle.Red > 40 && middle.Blue > 40, $"mélange attendu au centre : {middle}");
        var left = bitmap.GetPixel(0, 58);   // le bord est prolongé, pas assombri par du vide
        Assert.Equal(255, left.Alpha);
        Assert.True(left.Red > 200 && left.Blue < 40, $"bord gauche : {left}");
        var right = bitmap.GetPixel(479, 115);
        Assert.Equal(255, right.Alpha);
        Assert.True(right.Blue > 200 && right.Red < 40, $"bord droit : {right}");
    }

    [Fact]
    public void Get_Saturates()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(new SKColor(160, 100, 100));

        using var bitmap = SKBitmap.FromImage(backdrop.Get(background, Themes.LiquidGlass.Panel));

        var pixel = bitmap.GetPixel(240, 58);
        Assert.True(pixel.Red - pixel.Green > 60, $"la saturation doit écarter les canaux : {pixel}"); // 60 avant, ~102 après
    }

    [Fact]
    public void BrightestLuminance_MeasuresTheRectOnly()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(Halves(SKColors.White, SKColors.Black));

        float left = backdrop.BrightestLuminance(background, SKRect.Create(100, 100, 400, 200));
        float right = backdrop.BrightestLuminance(background, SKRect.Create(1300, 100, 400, 200));
        float both = backdrop.BrightestLuminance(background, SKRect.Create(560, 100, 800, 200));

        Assert.Equal(1f, left, 2);
        Assert.Equal(0f, right, 2);
        Assert.Equal(1f, both, 2); // la zone la plus claire, pas la moyenne
    }

    [Fact]
    public void BrightestLuminance_OneBrightCellInADarkPanel_IsFound()
    {
        using var backdrop = new Backdrop();
        // Panneau de 960 × 300 px (10 × 3 cellules) presque noir, avec un carré blanc de 120 px (une cellule).
        using var background = new StubBackground(StubBackground.Make(canvas =>
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(SKRect.Create(480, 96, 120, 96), paint);
        }));

        float value = backdrop.BrightestLuminance(background, SKRect.Create(100, 80, 960, 300));

        Assert.True(value > 0.5f, $"la cellule claire doit compter, pas la moyenne : {value}");
    }

    [Fact]
    public void BrightestLuminance_IsCachedPerVersionAndRect()
    {
        using var backdrop = new Backdrop();
        using var background = new StubBackground(SKColors.White);
        var rect = SKRect.Create(100, 100, 400, 200);
        float first = backdrop.BrightestLuminance(background, rect);

        background.ReplaceSilently(StubBackground.Make(c => c.Clear(SKColors.Black))); // même version : valeur gardée
        Assert.Equal(first, backdrop.BrightestLuminance(background, rect));

        background.Replace(StubBackground.Make(c => c.Clear(SKColors.Black)));          // nouvelle version : recalculée
        Assert.Equal(0f, backdrop.BrightestLuminance(background, rect), 2);
    }
}
