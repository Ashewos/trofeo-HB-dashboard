using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Layout;

/// <summary>Disposition complète du tableau de bord : rotation de l'écran et modules placés.</summary>
public sealed record DashboardLayout(int Rotation, IReadOnlyList<ModulePlacement> Placements)
{
    /// <summary>Disposition par défaut : rotation 180° (montage d'Alex) et une Heure en haut à droite.</summary>
    public static DashboardLayout Default { get; } = new(
        180,
        [
            new ModulePlacement(
                new Guid("5b0c5e2e-6a53-4c0b-9d0e-3f1a7c1d2b01"),
                "clock", 12, 0, 4, 2, TextSize.Normal,
                JsonDocument.Parse("{}").RootElement.Clone()),
        ]);
}
