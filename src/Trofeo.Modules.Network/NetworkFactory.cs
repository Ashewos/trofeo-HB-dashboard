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

    /// <summary>
    /// Fabrique de production : <c>Pc</c> donne la carte réseau du PC, <c>Freebox</c> la Freebox (par
    /// <paramref name="http"/>, le client HTTP partagé de l'hôte), <c>UniFi</c> une source qui reste « non configurée »
    /// en attendant son implémentation.
    /// </summary>
    public static NetworkFactory CreateProduction(HttpClient http) =>
        new((settings, ctx) => settings.Source switch
        {
            NetworkSourceKind.Freebox => new FreeboxSource(http, ctx.Secrets, ctx.Log, time: ctx.Time),
            NetworkSourceKind.UniFi => new UniFiPlaceholderSource(),
            _ => new NicSource(NicSource.ReadSystemCounters, ctx.Time),
        });

    public IModule Create(ModuleContext context)
    {
        var settings = NetworkSettings.FromJson(context.Settings);
        return new NetworkModule(Descriptor, createSource(settings, context), context.Time, context.Log);
    }

    /// <summary>Source UniFi provisoire : toujours « non configurée ».</summary>
    private sealed class UniFiPlaceholderSource : IThroughputSource
    {
        public string Title => "UniFi";

        public TimeSpan Interval => TimeSpan.FromSeconds(5);

        public Task<SourceReading> ReadAsync(CancellationToken ct) =>
            Task.FromResult(new SourceReading(null, new ModuleStatus(ModuleHealth.NotConfigured, "UniFi n'est pas encore disponible")));

        public void Dispose()
        {
        }
    }
}
