using SkiaSharp;
using Trofeo.Control;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.Tests;

/// <summary>Des réponses du service toutes prêtes, pour les tests de la fenêtre.</summary>
public static class SampleData
{
    public static Welcome Welcome() => new("0.5.0", ControlPipe.ProtocolVersion, Types());

    private static ModuleTypeInfo Type(string id, string name, GridSize min, GridSize compact, GridSize ideal, GridSize large) =>
        new(id, name, min, new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = compact,
            [SizePreset.Ideal] = ideal,
            [SizePreset.Large] = large,
        }, false, SettingsSchema.Empty);

    /// <summary>Les six types du service, avec leurs noms et leurs tailles réels.</summary>
    public static IReadOnlyList<ModuleTypeInfo> Types() =>
    [
        Type("claudecode", "Claude Code", new(6, 3), new(6, 3), new(8, 4), new(10, 4)),
        Type("codex", "Codex", new(6, 3), new(6, 3), new(8, 4), new(10, 4)),
        Type("monitoring", "Monitoring PC", new(3, 2), new(4, 2), new(6, 2), new(6, 4)),
        Type("nowplaying", "En écoute", new(3, 1), new(3, 1), new(4, 2), new(6, 2)),
        Type("clock", "Heure, date et météo", new(3, 1), new(3, 1), new(4, 2), new(6, 2)),
        Type("network", "Réseau", new(3, 1), new(3, 1), new(4, 2), new(6, 2)),
    ];

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
