using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>Image fixe (PNG, JPEG, WebP, GIF… : première image), recadrée pour remplir l'écran.</summary>
public sealed class ImageBackground : IBackground
{
    private readonly SKImage _image;

    private ImageBackground(SKImage image, SKSizeI decodedSize)
    {
        _image = image;
        DecodedSize = decodedSize;
    }

    /// <summary>Taille à laquelle le fichier a été décodé (réduite pour une grande image). Pour les tests.</summary>
    internal SKSizeI DecodedSize { get; }

    /// <summary>Rend null, avec un avertissement, si le fichier manque ou est illisible.</summary>
    public static ImageBackground? TryLoad(string path, Action<string>? warn)
    {
        using var codec = BackgroundSupport.OpenCodec(path, warn);
        if (codec is null) return null;

        SKImage? image;
        SKSizeI decoded;
        try
        {
            image = BackgroundSupport.DecodeCover(codec, out decoded);
        }
        catch (Exception e) when (e is IOException or OutOfMemoryException or InvalidOperationException)
        {
            image = null;
            decoded = default;
        }

        if (image is null)
        {
            warn?.Invoke(BackgroundSupport.Unreadable(path));
            return null;
        }
        return new ImageBackground(image, decoded);
    }

    public bool IsAnimated => false;
    public SKImage Current => _image;
    public int Version => 1;
    public bool Advance(TimeSpan elapsed) => false;
    public TimeSpan? UntilNextFrame => null;

    public void Dispose() => _image.Dispose();
}
