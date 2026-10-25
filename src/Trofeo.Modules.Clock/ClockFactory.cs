using Trofeo.ModuleKit;

namespace Trofeo.Modules.Clock;

/// <summary>Fabrique du module Heure, date et météo ; la météo vient de <paramref name="weather"/>.</summary>
public sealed class ClockFactory(IWeatherProvider weather) : IModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "clock",
        "Heure, date et météo",
        new GridSize(3, 1),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(3, 1),
            [SizePreset.Ideal] = new GridSize(4, 2),
            [SizePreset.Large] = new GridSize(6, 2),
        });

    public IModule Create(ModuleContext context) =>
        new ClockModule(Descriptor, ClockSettings.FromJson(context.Settings), weather, context.Time, context.Log);
}
