using SkiaSharp;
using Trofeo.Control;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.Tests;

/// <summary>Des réponses du service toutes prêtes, pour les tests de la fenêtre.</summary>
public static class SampleData
{
    public static Welcome Welcome() => new("0.5.0", ControlPipe.ProtocolVersion, []);

    public static ServiceStatus Status(LinkScreenState screen = LinkScreenState.Ready, bool emulated = false) => new(
        new ScreenInfo(screen, null), emulated, [],
        new ProcessInfo(1_000_000, 2_000_000, 1.5), new FrameInfo(10, 4, 1), new SensorInfo(true, true, true));

    /// <summary>Une image de l'écran (JPEG 1920 × 462) d'une couleur unie.</summary>
    public static byte[] Jpeg(SKColor color)
    {
        using var bitmap = new SKBitmap(1920, 462);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }
}
