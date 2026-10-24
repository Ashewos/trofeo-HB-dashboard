namespace Trofeo.ModuleKit;

/// <summary>Description statique d'un type de module.</summary>
public sealed record ModuleDescriptor(
    string TypeId,
    string DisplayName,
    GridSize MinSize,
    IReadOnlyDictionary<SizePreset, GridSize> Presets);
