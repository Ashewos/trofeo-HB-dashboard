using Trofeo.ModuleKit;

namespace Trofeo.Modules.Clock;

/// <summary>Fabrique du module Heure et date.</summary>
public sealed class ClockFactory : IModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "clock",
        "Heure et date",
        new GridSize(3, 1),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(3, 1),
            [SizePreset.Ideal] = new GridSize(4, 2),
            [SizePreset.Large] = new GridSize(6, 2),
        });

    public IModule Create(ModuleContext context) =>
        new ClockModule(Descriptor, ClockSettings.FromJson(context.Settings), context.Time);
}
