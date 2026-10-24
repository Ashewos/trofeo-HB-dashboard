using Trofeo.ModuleKit;

namespace Trofeo.Core.Layout;

/// <summary>Résultat de la validation d'un emplacement.</summary>
public enum PlacementError { None, OutOfBounds, Overlap, BelowMinSize, UnknownType }

/// <summary>Règles de placement : dans le damier, pas de chevauchement, taille minimum respectée.</summary>
public static class LayoutValidator
{
    /// <summary>Vérifie un candidat ; l'instance de même <c>InstanceId</c> dans la disposition est ignorée.</summary>
    public static PlacementError Check(
        DashboardLayout layout,
        ModulePlacement candidate,
        IReadOnlyDictionary<string, ModuleDescriptor> types)
    {
        if (!types.TryGetValue(candidate.TypeId, out var descriptor))
            return PlacementError.UnknownType;

        if (candidate.Column < 0 || candidate.Row < 0 ||
            candidate.Columns < 1 || candidate.Rows < 1 ||
            candidate.Column + candidate.Columns > GridGeometry.Columns ||
            candidate.Row + candidate.Rows > GridGeometry.Rows)
            return PlacementError.OutOfBounds;

        if (candidate.Columns < descriptor.MinSize.Columns || candidate.Rows < descriptor.MinSize.Rows)
            return PlacementError.BelowMinSize;

        foreach (var other in layout.Placements)
        {
            if (other.InstanceId == candidate.InstanceId) continue;
            if (Intersects(candidate, other)) return PlacementError.Overlap;
        }

        return PlacementError.None;
    }

    private static bool Intersects(ModulePlacement a, ModulePlacement b) =>
        a.Column < b.Column + b.Columns && b.Column < a.Column + a.Columns &&
        a.Row < b.Row + b.Rows && b.Row < a.Row + a.Rows;
}
