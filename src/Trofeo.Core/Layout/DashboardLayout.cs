using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Layout;

/// <summary>Disposition complète du tableau de bord : rotation de l'écran et modules placés.</summary>
public sealed record DashboardLayout(int Rotation, IReadOnlyList<ModulePlacement> Placements)
{
    /// <summary>
    /// Disposition par défaut : rotation 180° (montage d'Alex), Monitoring et En écoute au milieu, Heure et Réseau
    /// à droite. Les colonnes 0 à 7 restent libres pour Claude Code (plan 3).
    /// </summary>
    public static DashboardLayout Default { get; } = new(
        180,
        [
            new ModulePlacement(
                new Guid("246ac06d-eec2-4d64-9930-24b64603a03b"),
                "monitoring", 8, 0, 4, 2, TextSize.Normal, Settings("{}")),
            new ModulePlacement(
                new Guid("8ebcc647-ab4b-401d-b2b3-f7a0ebd97b4b"),
                "nowplaying", 8, 2, 4, 2, TextSize.Normal, Settings("{}")),
            new ModulePlacement(
                new Guid("5b0c5e2e-6a53-4c0b-9d0e-3f1a7c1d2b01"),
                "clock", 12, 0, 4, 2, TextSize.Normal, Settings("{}")),
            new ModulePlacement(
                new Guid("a2002423-c807-4403-9c5a-2a983fb6a371"),
                "network", 12, 2, 4, 2, TextSize.Normal, Settings("""{"source":"pc"}""")),
        ]);

    private static JsonElement Settings(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
