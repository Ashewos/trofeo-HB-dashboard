using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public sealed class BackgroundTests : IDisposable
{
    private static readonly SKColor Red = new(255, 0, 0);
    private static readonly SKColor Green = new(0, 255, 0);
    private static readonly SKColor Blue = new(0, 0, 255);

    private readonly string _dir = Directory.CreateTempSubdirectory("trofeo-bg-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SKColor Pixel(SKImage image, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(image);
        return bmp.GetPixel(x, y);
    }

    private static void AssertNear(SKColor expected, SKColor actual, int tolerance = 6)
    {
        Assert.True(
            Math.Abs(expected.Red - actual.Red) <= tolerance &&
            Math.Abs(expected.Green - actual.Green) <= tolerance &&
            Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"Attendu {expected}, obtenu {actual}.");
    }

    private static double Distance(SKColor a, SKColor b) =>
        Math.Sqrt(Math.Pow(a.Red - b.Red, 2) + Math.Pow(a.Green - b.Green, 2) + Math.Pow(a.Blue - b.Blue, 2));

    private string WritePng(string name, int width, int height, Action<SKCanvas> draw)
    {
        var path = Path.Combine(_dir, name);
        using var bmp = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bmp)) draw(canvas);
        using var data = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private string WriteGif(string name, int[] delaysCs, params SKColor[] frames)
    {
        var path = Path.Combine(_dir, name);
        TinyGif.Write(path, frames, delaysCs);
        return path;
    }

    // ---- Couleur ----

    [Fact]
    public void Color_FillsScreen()
    {
        using var bg = new ColorBackground(SKColor.Parse("#336699"));

        Assert.Equal(GridGeometry.Width, bg.Current.Width);
        Assert.Equal(GridGeometry.Height, bg.Current.Height);
        Assert.Equal(SKColor.Parse("#336699"), Pixel(bg.Current, 0, 0));
        Assert.Equal(SKColor.Parse("#336699"), Pixel(bg.Current, 1919, 461));
        Assert.Equal(SKColor.Parse("#336699"), Pixel(bg.Current, 960, 231));
        Assert.False(bg.IsAnimated);
        Assert.Null(bg.UntilNextFrame);
        Assert.False(bg.Advance(TimeSpan.FromSeconds(5)));
        Assert.Same(bg.Current, bg.Current);
    }

    // ---- Aurore ----

    [Fact]
    public void Aurora_HasMockupColors()
    {
        using var bg = new AuroraBackground();

        Assert.Equal(1920, bg.Current.Width);
        Assert.Equal(462, bg.Current.Height);
        Assert.False(bg.IsAnimated);
        Assert.Null(bg.UntilNextFrame);

        // Centre du halo orange : 12 % de 1920, 85 % de 462.
        var orange = Pixel(bg.Current, 230, 393);
        Assert.True(Distance(orange, SKColor.Parse("#FF7A59")) < Distance(orange, SKColor.Parse("#241A3D")));
        AssertNear(SKColor.Parse("#FF7A59"), orange, 12);

        // Centre du halo turquoise (48 %, 10 %) et du halo violet (82 %, 90 %).
        AssertNear(SKColor.Parse("#1FB8A6"), Pixel(bg.Current, 922, 46), 12);
        AssertNear(SKColor.Parse("#6D5DFC"), Pixel(bg.Current, 1574, 416), 12);
    }

    [Fact]
    public void Aurora_BlendsLinearGradientOutsideTheHalos()
    {
        using var bg = new AuroraBackground();

        // Loin de tous les halos : le dégradé linéaire reste sombre (jamais le blanc, jamais transparent).
        var p = Pixel(bg.Current, 1000, 230);
        Assert.Equal(255, p.Alpha);
        Assert.True(p.Red < 140 && p.Green < 140 && p.Blue < 140, $"Trop clair : {p}");
    }

    // ---- Image ----

    [Fact]
    public void Image_CoverCrop()
    {
        var path = WritePng("cover.png", 100, 100, c =>
        {
            c.Clear(Blue);
            using var paint = new SKPaint { Color = Red };
            c.DrawRect(0, 0, 100, 50, paint);
        });
        var warnings = new List<string>();

        using var bg = ImageBackground.TryLoad(path, warnings.Add)!;

        Assert.NotNull(bg);
        Assert.Empty(warnings);
        Assert.Equal(1920, bg.Current.Width);
        Assert.Equal(462, bg.Current.Height);
        Assert.False(bg.IsAnimated);
        Assert.Null(bg.UntilNextFrame);
        AssertNear(Red, Pixel(bg.Current, 960, 5));
        AssertNear(Blue, Pixel(bg.Current, 960, 456));
    }

    [Fact]
    public void Image_Huge_IsDecodedScaledDown()
    {
        var path = Path.Combine(_dir, "huge.jpg");
        using (var bmp = new SKBitmap(8000, 2000))
        {
            bmp.Erase(new SKColor(40, 90, 160));
            using var data = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Jpeg, 20);
            File.WriteAllBytes(path, data.ToArray());
        }

        using var bg = ImageBackground.TryLoad(path, null)!;

        Assert.NotNull(bg);
        Assert.Equal(1920, bg.Current.Width);
        Assert.Equal(462, bg.Current.Height);
        Assert.True(bg.DecodedSize.Width >= 1920, $"Décodée trop petite : {bg.DecodedSize}");
        Assert.True(bg.DecodedSize.Width < 4000, $"Décodée trop grande : {bg.DecodedSize}");
        AssertNear(new SKColor(40, 90, 160), Pixel(bg.Current, 960, 231), 12);
    }

    [Fact]
    public void Image_TooLarge_IsRefused()
    {
        var path = WritePng("long.png", 16385, 1, c => c.Clear(Red));
        var warnings = new List<string>();

        var bg = ImageBackground.TryLoad(path, warnings.Add);

        Assert.Null(bg);
        Assert.Equal([$"Fond d'écran illisible : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Image_Missing_Warns()
    {
        var path = Path.Combine(_dir, "absent.png");
        var warnings = new List<string>();

        Assert.Null(ImageBackground.TryLoad(path, warnings.Add));
        Assert.Equal([$"Fond d'écran introuvable : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Image_Garbage_Warns()
    {
        var path = Path.Combine(_dir, "garbage.png");
        File.WriteAllText(path, "ceci n'est pas une image");
        var warnings = new List<string>();

        Assert.Null(ImageBackground.TryLoad(path, warnings.Add));
        Assert.Equal([$"Fond d'écran illisible : {path}. Fond du thème utilisé."], warnings);
    }

    // ---- Fabrique ----

    [Fact]
    public void Factory_NullOrTheme_UsesThemeBackground()
    {
        using var classique = BackgroundFactory.Create(null, Themes.ClassiqueDefinition);
        using var themed = BackgroundFactory.Create(new BackgroundSettings(BackgroundKind.Theme), Themes.ClassiqueDefinition);
        using var glass = BackgroundFactory.Create(null, Themes.LiquidGlass);

        Assert.IsType<ColorBackground>(classique);
        Assert.Equal(SKColor.Parse("#0E1116"), Pixel(classique.Current, 5, 5));
        Assert.IsType<ColorBackground>(themed);
        Assert.IsType<AuroraBackground>(glass);
    }

    [Fact]
    public void Factory_Color_UsesIt()
    {
        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Color, Color: "#102030"), Themes.ClassiqueDefinition);

        Assert.IsType<ColorBackground>(bg);
        Assert.Equal(SKColor.Parse("#102030"), Pixel(bg.Current, 100, 100));
    }

    [Fact]
    public void Factory_MissingFile_FallsBackToTheme()
    {
        var path = Path.Combine(_dir, "nulle-part.png");
        var warnings = new List<string>();

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Image, Path: path), Themes.ClassiqueDefinition, warn: warnings.Add);

        Assert.IsType<ColorBackground>(bg);
        Assert.Equal(SKColor.Parse("#0E1116"), Pixel(bg.Current, 5, 5));
        Assert.Equal([$"Fond d'écran introuvable : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Factory_UnreadableAnimated_FallsBackToTheme()
    {
        var path = Path.Combine(_dir, "cassé.gif");
        File.WriteAllText(path, "GIF89a mais pas vraiment");
        var warnings = new List<string>();

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Animated, Path: path), Themes.LiquidGlass, warn: warnings.Add);

        Assert.IsType<AuroraBackground>(bg);
        Assert.Equal([$"Fond d'écran illisible : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Factory_NoPath_FallsBackToTheme()
    {
        var warnings = new List<string>();

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Image), Themes.ClassiqueDefinition, warn: warnings.Add);

        Assert.IsType<ColorBackground>(bg);
        Assert.Equal(["Fond d'écran introuvable : . Fond du thème utilisé."], warnings);
    }

    [Theory]
    [InlineData("pas-une-couleur")]
    [InlineData("")]
    [InlineData(null)]
    public void Factory_BadColor_FallsBack(string? color)
    {
        var warnings = new List<string>();

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Color, Color: color), Themes.ClassiqueDefinition, warn: warnings.Add);

        Assert.IsType<ColorBackground>(bg);
        Assert.Equal(SKColor.Parse("#0E1116"), Pixel(bg.Current, 5, 5));
        Assert.Equal([$"Couleur de fond invalide : {color}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Factory_Video_WithoutHost_FallsBack()
    {
        var warnings = new List<string>();

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Video, Path: "C:\\clip.mp4"), Themes.ClassiqueDefinition, warn: warnings.Add);

        Assert.IsType<ColorBackground>(bg);
        Assert.Equal(["Vidéo non prise en charge ici : C:\\clip.mp4. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Factory_Video_HostReturnsNull_FallsBack()
    {
        var warnings = new List<string>();

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Video, Path: "clip.mp4"), Themes.ClassiqueDefinition,
            video: _ => null, warn: warnings.Add);

        Assert.IsType<ColorBackground>(bg);
        Assert.Equal(["Vidéo non prise en charge ici : clip.mp4. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Factory_Video_UsesHostBackground()
    {
        var warnings = new List<string>();
        using var fake = new ColorBackground(Red);

        var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Video, Path: "clip.mp4"), Themes.ClassiqueDefinition,
            video: p => p == "clip.mp4" ? fake : null, warn: warnings.Add);

        Assert.Same(fake, bg);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Factory_Animated_PlaysGif()
    {
        var path = WriteGif("a.gif", [20, 20], Red, Green);

        using var bg = BackgroundFactory.Create(
            new BackgroundSettings(BackgroundKind.Animated, Path: path), Themes.ClassiqueDefinition);

        Assert.IsType<AnimatedBackground>(bg);
        Assert.True(bg.IsAnimated);
    }

    // ---- Image animée ----

    [Fact]
    public void Animated_FollowsElapsedTime()
    {
        var path = WriteGif("rgb.gif", [20, 20, 20], Red, Green, Blue);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        Assert.NotNull(bg);
        Assert.True(bg.IsAnimated);
        Assert.Equal(1920, bg.Current.Width);
        Assert.Equal(462, bg.Current.Height);
        AssertNear(Red, Pixel(bg.Current, 960, 231));
        Assert.Equal(TimeSpan.FromMilliseconds(200), bg.UntilNextFrame);

        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(250)));
        AssertNear(Green, Pixel(bg.Current, 960, 231));
        Assert.Equal(TimeSpan.FromMilliseconds(150), bg.UntilNextFrame);

        Assert.False(bg.Advance(TimeSpan.FromMilliseconds(300)));
        AssertNear(Green, Pixel(bg.Current, 960, 231));
        Assert.Equal(TimeSpan.FromMilliseconds(100), bg.UntilNextFrame);

        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(450)));
        AssertNear(Blue, Pixel(bg.Current, 960, 231));

        // Bouclage : 650 ms = 600 + 50 → première image.
        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(650)));
        AssertNear(Red, Pixel(bg.Current, 960, 231));
    }

    [Fact]
    public void Animated_FastGif_DropsFramesKeepsSpeed()
    {
        var colors = Enumerable.Range(0, 10).Select(i => new SKColor((byte)(20 * i + 10), 0, 0)).ToArray();
        var path = WriteGif("fast.gif", Enumerable.Repeat(2, 10).ToArray(), colors);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(100)));

        AssertNear(colors[5], Pixel(bg.Current, 960, 231), 2);
        Assert.True(bg.UntilNextFrame <= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void Animated_ZeroDelay_Uses100Ms()
    {
        var path = WriteGif("zero.gif", [0, 0, 0], Red, Green, Blue);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(150)));

        AssertNear(Green, Pixel(bg.Current, 960, 231));
        Assert.Equal(TimeSpan.FromMilliseconds(50), bg.UntilNextFrame);
    }

    [Fact]
    public void Animated_ShortDelay_Uses100Ms()
    {
        // 1 cs = 10 ms < 20 ms : vaut 100 ms, comme les navigateurs.
        var path = WriteGif("short.gif", [1, 1], Red, Green);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        Assert.Equal(TimeSpan.FromMilliseconds(100), bg.UntilNextFrame);
    }

    [Fact]
    public void Animated_KeepsOneDecodedFrame()
    {
        var colors = Enumerable.Range(0, 12).Select(i => new SKColor((byte)(20 * i), 50, 50)).ToArray();
        var path = WriteGif("many.gif", Enumerable.Repeat(5, 12).ToArray(), colors);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        // Chaque image recadrée est écrite dans les mêmes pixels (pas de nouveau bitmap de 3,5 Mo par image).
        var address = bg.Current.PeekPixels().GetPixels();
        Assert.NotEqual(IntPtr.Zero, address);
        int changes = 0;
        for (int ms = 0; ms < 3000; ms += 70)
        {
            if (!bg.Advance(TimeSpan.FromMilliseconds(ms))) continue;
            changes++;
            Assert.Equal(address, bg.Current.PeekPixels().GetPixels());
            Assert.Equal(2, bg.BitmapCount);
        }
        Assert.True(changes > 10);
        Assert.Equal(2, bg.BitmapCount);
    }

    [Fact]
    public void Animated_Version_ChangesOnlyWithFrame()
    {
        var path = WriteGif("v.gif", [20, 20], Red, Green);
        using var bg = AnimatedBackground.TryLoad(path, null)!;
        int v0 = bg.Version;
        var image0 = bg.Current;

        Assert.False(bg.Advance(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(v0, bg.Version);
        Assert.Same(image0, bg.Current);

        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(250)));
        Assert.NotEqual(v0, bg.Version);
        int v1 = bg.Version;

        Assert.False(bg.Advance(TimeSpan.FromMilliseconds(260)));
        Assert.Equal(v1, bg.Version);

        // Retour en arrière (nouveau bouclage) : nouvelle image, nouvelle version.
        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(410)));
        Assert.NotEqual(v1, bg.Version);
    }

    [Fact]
    public void Animated_DependentFrames_KeepPriorPixels()
    {
        // Les points de contrôle évitent les frontières entre carrés (filtrage linéaire). Image 0 : tout rouge. Image 1 : carré vert en haut à gauche, par-dessus. Image 2 : carré bleu en bas à droite.
        var path = Path.Combine(_dir, "patches.gif");
        TinyGif.WritePatches(path, [(Red, 0, 0, 4, 4), (Green, 0, 0, 2, 2), (Blue, 2, 2, 2, 2)], [20, 20, 20]);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        // Saut direct de 0 à 2 : il faut reconstruire l'image 1 dans le tampon avant de décoder la 2.
        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(450)));
        AssertNear(Green, Pixel(bg.Current, 100, 0), 16);
        AssertNear(Red, Pixel(bg.Current, 1800, 0), 16);
        AssertNear(Blue, Pixel(bg.Current, 1800, 461), 16);

        // Bouclage : retour à l'image 0, puis image 1 sans la 2.
        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(600)));
        AssertNear(Red, Pixel(bg.Current, 100, 0), 16);
        Assert.True(bg.Advance(TimeSpan.FromMilliseconds(850)));
        AssertNear(Green, Pixel(bg.Current, 100, 0), 16);
        AssertNear(Red, Pixel(bg.Current, 1800, 461), 16);
        Assert.Equal(2, bg.BitmapCount);
    }

    [Fact]
    public void Animated_SingleFrame_IsStill()
    {
        var path = WriteGif("one.gif", [10], Blue);
        using var bg = AnimatedBackground.TryLoad(path, null)!;

        Assert.NotNull(bg);
        Assert.False(bg.IsAnimated);
        Assert.Null(bg.UntilNextFrame);
        Assert.False(bg.Advance(TimeSpan.FromSeconds(10)));
        AssertNear(Blue, Pixel(bg.Current, 960, 231));
    }

    [Fact]
    public void Animated_HugeFrame_IsRefusedBeforeAllocating()
    {
        // Écran logique de 10 000 × 10 000 (> 4096 × 4096) : refusé sans allouer le tampon de décodage (400 Mo).
        var path = Path.Combine(_dir, "enorme.gif");
        TinyGif.WritePatches(path, [(Red, 0, 0, 4, 4), (Green, 0, 0, 2, 2)], [10, 10], screenSize: 10000);
        var warnings = new List<string>();

        var bg = AnimatedBackground.TryLoad(path, warnings.Add);

        Assert.Null(bg);
        Assert.Equal([$"Fond d'écran illisible : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Animated_Missing_Warns()
    {
        var path = Path.Combine(_dir, "absent.gif");
        var warnings = new List<string>();

        Assert.Null(AnimatedBackground.TryLoad(path, warnings.Add));
        Assert.Equal([$"Fond d'écran introuvable : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Animated_Dispose_ReleasesImage()
    {
        var path = WriteGif("d.gif", [10, 10], Red, Green);
        var bg = AnimatedBackground.TryLoad(path, null)!;
        bg.Dispose();
        bg.Dispose(); // idempotent

        Assert.Equal(0, bg.BitmapCount);
    }
}
