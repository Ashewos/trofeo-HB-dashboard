using SkiaSharp;
using Trofeo.Core.Layout;

namespace Trofeo.Core.Rendering;

/// <summary>Fond de l'écran, toujours 1920 × 462. Utilisé par un seul thread (celui de l'affichage).</summary>
public interface IBackground : IDisposable
{
    bool IsAnimated { get; }

    /// <summary>
    /// Image courante, recadrée pour remplir l'écran. Même instance tant que <see cref="Version"/> ne change pas ;
    /// après un <see cref="Advance"/> qui rend vrai, l'ancienne instance n'est plus valable.
    /// </summary>
    SKImage Current { get; }

    /// <summary>Change à chaque nouvelle image (le flou du verre s'appuie dessus).</summary>
    int Version { get; }

    /// <summary>Avance au temps écoulé depuis le début de la lecture ; vrai si l'image a changé.</summary>
    bool Advance(TimeSpan elapsed);

    /// <summary>Délai avant la prochaine image depuis le dernier <see cref="Advance"/> ; null pour un fond fixe.</summary>
    TimeSpan? UntilNextFrame { get; }
}

/// <summary>Aides communes aux fonds : recadrage, décodage d'une image fixe, textes d'avertissement.</summary>
internal static class BackgroundSupport
{
    /// <summary>Au-delà de cette taille (par côté), une image est refusée.</summary>
    public const int MaxSide = 16384;

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static SKImageInfo ScreenInfo { get; } =
        new(GridGeometry.Width, GridGeometry.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

    public static string Missing(string path) => $"Fond d'écran introuvable : {path}. Fond du thème utilisé.";
    public static string Unreadable(string path) => $"Fond d'écran illisible : {path}. Fond du thème utilisé.";
    public static string BadColor(string? color) => $"Couleur de fond invalide : {color}. Fond du thème utilisé.";
    public static string NoVideo(string? path) => $"Vidéo non prise en charge ici : {path}. Fond du thème utilisé.";

    /// <summary>Rend immuable puis enveloppe l'image : les pixels sont partagés, le bitmap peut être libéré.</summary>
    public static SKImage Freeze(SKBitmap bitmap)
    {
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    /// Dessine <paramref name="source"/> sur tout l'écran, centrée : échelle = max(1920 / l, 462 / h)
    /// (l'image remplit l'écran, le surplus est coupé).
    /// </summary>
    public static void DrawCover(SKCanvas canvas, SKImage source)
    {
        float scale = Math.Max((float)GridGeometry.Width / source.Width, (float)GridGeometry.Height / source.Height);
        float w = source.Width * scale, h = source.Height * scale;
        var dest = SKRect.Create((GridGeometry.Width - w) / 2f, (GridGeometry.Height - h) / 2f, w, h);
        canvas.DrawImage(source, new SKRect(0, 0, source.Width, source.Height), dest, Sampling, null);
    }

    /// <summary>
    /// Ouvre le fichier. Rend null, après avoir averti, s'il manque, s'il n'est pas une image ou si un côté dépasse
    /// <see cref="MaxSide"/>.
    /// </summary>
    public static SKCodec? OpenCodec(string path, Action<string>? warn)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            warn?.Invoke(Missing(path));
            return null;
        }

        SKCodec? codec = null;
        try
        {
            codec = SKCodec.Create(path);
            if (codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0 &&
                codec.Info.Width <= MaxSide && codec.Info.Height <= MaxSide)
            {
                return codec;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fichier verrouillé ou chemin invalide : traité comme illisible.
        }

        codec?.Dispose();
        warn?.Invoke(Unreadable(path));
        return null;
    }

    /// <summary>
    /// Décode la première image à la plus petite échelle qui couvre encore l'écran (1/8, 1/4, 1/2 ou 1 ; les formats
    /// sans décodage réduit, comme PNG, sont décodés en taille réelle), puis la recadre en 1920 × 462.
    /// </summary>
    public static SKImage? DecodeCover(SKCodec codec, out SKSizeI decodedSize)
    {
        decodedSize = codec.Info.Size;
        foreach (var fraction in new[] { 1f / 8, 1f / 4, 1f / 2 })
        {
            var scaled = codec.GetScaledDimensions(fraction);
            if (scaled.Width >= GridGeometry.Width && scaled.Height >= GridGeometry.Height)
            {
                decodedSize = scaled;
                break;
            }
        }

        using var decoded = new SKBitmap(new SKImageInfo(decodedSize.Width, decodedSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) return null;
        decoded.SetImmutable();

        using var source = SKImage.FromBitmap(decoded);
        using var screen = new SKBitmap(ScreenInfo);
        using (var canvas = new SKCanvas(screen))
        {
            canvas.Clear(SKColors.Black); // un fond transparent (PNG, GIF) ne doit pas laisser voir à travers l'écran
            DrawCover(canvas, source);
        }
        return Freeze(screen);
    }
}
