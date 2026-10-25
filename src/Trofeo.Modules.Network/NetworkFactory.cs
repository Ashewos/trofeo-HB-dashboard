using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>
/// Fabrique du module Réseau. Les réglages sont lus ici ; <paramref name="createSource"/> choisit la source de débit
/// correspondante.
/// </summary>
public sealed class NetworkFactory(Func<NetworkSettings, ModuleContext, IThroughputSource> createSource) : IModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "network",
        "Réseau",
        new GridSize(3, 1),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(3, 1),
            [SizePreset.Ideal] = new GridSize(4, 2),
            [SizePreset.Large] = new GridSize(6, 2),
        });

    /// <summary>Fabrique provisoire : toute source donne la carte réseau du PC, en attendant Freebox et UniFi.</summary>
    public static NetworkFactory CreateDefault() =>
        new((_, ctx) => new NicSource(NicSource.ReadSystemCounters, ctx.Time));

    public IModule Create(ModuleContext context)
    {
        var settings = NetworkSettings.FromJson(context.Settings);
        return new NetworkModule(Descriptor, createSource(settings, context), context.Time, context.Log);
    }
}
