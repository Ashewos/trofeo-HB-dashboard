using Trofeo.ModuleKit;
using Trofeo.Sensors;

namespace Trofeo.Modules.Monitoring;

/// <summary>Fabrique du module Monitoring PC, alimenté par la source de relevés partagée.</summary>
public sealed class MonitoringFactory(ISensorFeed feed) : IModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "monitoring",
        "Monitoring PC",
        new GridSize(3, 2),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(4, 2),
            [SizePreset.Ideal] = new GridSize(6, 2),
            [SizePreset.Large] = new GridSize(6, 4),
        });

    public IModule Create(ModuleContext context) =>
        new MonitoringModule(Descriptor, MonitoringSettings.FromJson(context.Settings), feed, context.Time);
}
