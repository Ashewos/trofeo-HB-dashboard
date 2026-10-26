namespace Trofeo.ModuleKit;

/// <summary>Description statique d'un type de module. <c>AcceptsCommands</c> : le module réagit aux raccourcis (<see cref="ICommandableModule"/>).</summary>
public sealed record ModuleDescriptor(
    string TypeId,
    string DisplayName,
    GridSize MinSize,
    IReadOnlyDictionary<SizePreset, GridSize> Presets,
    bool AcceptsCommands = false);
