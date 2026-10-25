using SkiaSharp;
using Trofeo.Core.Layout;

namespace Trofeo.Core.Rendering;

/// <summary>
/// Copies du fond au quart de la taille (480 × 116), gardées tant que le fond ne change pas :
/// <list type="bullet">
/// <item>une copie nette, qui sert à mesurer la luminance sous un panneau (garde de lisibilité, tous les styles) ;</item>
/// <item>une copie floutée et saturée pour le verre, recalculée seulement quand l'instance du fond, sa
/// <see cref="IBackground.Version"/> ou le <see cref="PanelLook"/> changent.</item>
/// </list>
/// Rien ne garde l'image courante du fond d'une image à l'autre : celle d'un fond animé n'est valable que jusqu'au
/// prochain <see cref="IBackground.Advance"/>. Les copies vivent dans leurs propres pixels. Un seul thread.
/// </summary>
internal sealed class Backdrop : IDisposable
{
    /// <summary>Largeur et hauteur des copies : 1920 / 4 et 462 / 4, arrondi au-dessus.</summary>
    public const int Width = 480, Height = 116;

    private const float ScaleX = (float)Width / GridGeometry.Width;
    private const float ScaleY = (float)Height / GridGeometry.Height;

    private static readonly SKSamplingOptions Shrink = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly SKImageInfo SmallInfo = new(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);

    // Luminance linéaire de chaque valeur sRGB d'un octet.
    private static readonly float[] Linear = Enumerable.Range(0, 256).Select(v => PanelPainter.Linearize(v / 255f)).ToArray();

    private readonly SKBitmap _small = new(SmallInfo);
    private readonly Dictionary<SKRect, float> _luminances = [];
    private IBackground? _smallFor;
    private int _smallVersion;

    private SKSurface? _surface;
    private SKImage? _blurred;
    private IBackground? _blurFor;
    private int _blurVersion;
    private PanelLook? _blurLook;

    /// <summary>Nombre de fois que la copie floutée a été recalculée.</summary>
    internal int Computations { get; private set; }

    /// <summary>La copie floutée et saturée du fond, au quart de la taille ; valable jusqu'au prochain appel avec un autre fond.</summary>
    public SKImage Get(IBackground background, PanelLook look)
    {
        EnsureSmall(background);
        if (_blurred is not null && ReferenceEquals(_blurFor, background) && _blurVersion == background.Version && look.Equals(_blurLook))
        {
            return _blurred;
        }

        _surface ??= SKSurface.Create(SmallInfo)
            ?? throw new InvalidOperationException("Impossible de créer la surface du flou.");
        var canvas = _surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint();
        SKImageFilter? blur = null, filter = null;
        SKColorFilter? saturation = null;
        try
        {
            float sigma = look.BlurSigma / 4f;
            if (sigma > 0f)
            {
                // Clamp : les bords du fond sont prolongés, le flou ne s'assombrit pas vers l'extérieur.
                blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
            }
            if (look.Saturation != 1f)
            {
                saturation = SKColorFilter.CreateColorMatrix(PanelPainter.SaturationMatrix(look.Saturation));
                filter = SKImageFilter.CreateColorFilter(saturation, blur);
            }
            paint.ImageFilter = filter ?? blur;
            canvas.DrawBitmap(_small, 0, 0, paint);
        }
        finally
        {
            filter?.Dispose();
            saturation?.Dispose();
            blur?.Dispose();
        }

        _blurred?.Dispose();
        _blurred = _surface.Snapshot();
        _blurFor = background;
        _blurVersion = background.Version;
        _blurLook = look;
        Computations++;
        return _blurred;
    }

    /// <summary>
    /// Luminance relative moyenne (WCAG, 0 à 1) du fond sous <paramref name="rect"/> (pixels de l'écran). Calculée sur la
    /// copie réduite, une fois par version du fond et par rectangle.
    /// </summary>
    public float MeanLuminance(IBackground background, SKRect rect)
    {
        EnsureSmall(background);
        if (_luminances.TryGetValue(rect, out float cached))
        {
            return cached;
        }

        float value = MeanLuminance(_small, rect);
        _luminances[rect] = value;
        return value;
    }

    /// <summary>Luminance relative moyenne sous <paramref name="rect"/> d'une copie réduite à 480 × 116.</summary>
    internal static float MeanLuminance(SKBitmap small, SKRect rect)
    {
        int x0 = Math.Clamp((int)Math.Floor(rect.Left * ScaleX), 0, Width - 1);
        int x1 = Math.Clamp((int)Math.Ceiling(rect.Right * ScaleX), x0 + 1, Width);
        int y0 = Math.Clamp((int)Math.Floor(rect.Top * ScaleY), 0, Height - 1);
        int y1 = Math.Clamp((int)Math.Ceiling(rect.Bottom * ScaleY), y0 + 1, Height);

        var pixels = small.GetPixelSpan();
        int stride = small.RowBytes;
        double sum = 0;
        for (int y = y0; y < y1; y++)
        {
            int index = y * stride + x0 * 4;
            for (int x = x0; x < x1; x++, index += 4)
            {
                // Bgra8888 : B, G, R, A. Le fond est opaque.
                sum += 0.0722f * Linear[pixels[index]] + 0.7152f * Linear[pixels[index + 1]] + 0.2126f * Linear[pixels[index + 2]];
            }
        }
        return (float)(sum / ((x1 - x0) * (y1 - y0)));
    }

    /// <summary>Réduit au quart l'image de <paramref name="background"/> dans <paramref name="target"/> (480 × 116, Bgra).</summary>
    internal static void Shrunk(SKImage image, SKBitmap target)
    {
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawImage(image, new SKRect(0, 0, image.Width, image.Height), new SKRect(0, 0, Width, Height), Shrink, paint);
    }

    private void EnsureSmall(IBackground background)
    {
        if (ReferenceEquals(_smallFor, background) && _smallVersion == background.Version)
        {
            return;
        }

        Shrunk(background.Current, _small);
        _luminances.Clear();
        _smallFor = background;
        _smallVersion = background.Version;
    }

    public void Dispose()
    {
        _blurred?.Dispose();
        _surface?.Dispose();
        _small.Dispose();
        _blurred = null;
        _surface = null;
        _smallFor = _blurFor = null;
    }
}
