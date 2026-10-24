using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>Encode une image en JPEG 4:4:4 sous la limite de taille de l'écran.</summary>
public static class JpegEncoder
{
    public const int MaxBytes = 450_000;

    private const int QualityStep = 12;
    private const int MinQuality = 20;

    /// <summary>Applique la rotation (0 ou 180) puis encode ; baisse la qualité tant que le résultat dépasse <see cref="MaxBytes"/>.</summary>
    public static byte[] Encode(SKBitmap frame, int rotation, int quality = 88)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (rotation is not (0 or 180))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "La rotation doit valoir 0 ou 180.");
        }

        using var rotated = rotation == 180 ? Rotate180(frame) : null;
        var source = rotated ?? frame;

        using var pixmap = source.PeekPixels();
        int current = Math.Clamp(quality, MinQuality, 100);
        while (true)
        {
            var options = new SKJpegEncoderOptions(
                current, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore);
            using var data = pixmap.Encode(options)
                ?? throw new InvalidOperationException("L'encodage JPEG a échoué.");
            if (data.Size <= MaxBytes || current <= MinQuality)
            {
                return data.ToArray();
            }

            current = Math.Max(MinQuality, current - QualityStep);
        }
    }

    private static SKBitmap Rotate180(SKBitmap frame)
    {
        var result = new SKBitmap(frame.Info);
        using var canvas = new SKCanvas(result);
        canvas.Translate(frame.Width, frame.Height);
        canvas.RotateDegrees(180);
        canvas.DrawBitmap(frame, 0, 0);
        return result;
    }
}
