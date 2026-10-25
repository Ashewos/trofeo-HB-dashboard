using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>Fond uni. La transparence éventuelle de la couleur est ignorée : l'écran n'a pas de dessous.</summary>
public sealed class ColorBackground : IBackground
{
    private readonly SKImage _image;

    public ColorBackground(SKColor color)
    {
        using var bitmap = new SKBitmap(BackgroundSupport.ScreenInfo);
        bitmap.Erase(color.WithAlpha(255));
        _image = BackgroundSupport.Freeze(bitmap);
    }

    public bool IsAnimated => false;
    public SKImage Current => _image;
    public int Version => 1;
    public bool Advance(TimeSpan elapsed) => false;
    public TimeSpan? UntilNextFrame => null;

    public void Dispose() => _image.Dispose();
}
