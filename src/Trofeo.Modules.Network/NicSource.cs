using System.Net;
using System.Net.NetworkInformation;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>Compteurs d'octets cumulés d'une interface réseau.</summary>
public sealed record InterfaceCounters(string Id, long BytesReceived, long BytesSent);

/// <summary>
/// Débit des cartes réseau du PC : écart des compteurs cumulés entre deux lectures, divisé par le temps écoulé.
/// Le temps écoulé vient de l'horloge monotone : l'heure murale peut sauter (changement d'heure, synchronisation).
/// Appelée par le module sans chevauchement, donc sans verrou.
/// </summary>
public sealed class NicSource(Func<IReadOnlyList<InterfaceCounters>> readCounters, TimeProvider time) : IThroughputSource
{
    private Dictionary<string, InterfaceCounters>? _previous;
    private long _previousAt;

    public string Title => "Ce PC";

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public Task<SourceReading> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var counters = readCounters();
        var now = time.GetTimestamp();
        var current = new Dictionary<string, InterfaceCounters>(counters.Count);
        foreach (var item in counters) current[item.Id] = item;

        ThroughputSample? sample = null;
        var elapsed = time.GetElapsedTime(_previousAt, now).TotalSeconds;
        if (_previous is not null && elapsed > 0)
        {
            long down = 0, up = 0;
            foreach (var (id, after) in current)
            {
                // Une interface apparue depuis la dernière lecture n'a pas de point de départ : on l'ignore.
                if (!_previous.TryGetValue(id, out var before)) continue;

                // Un compteur qui redescend (carte réinitialisée) compte pour 0, jamais pour un débit négatif.
                down += Math.Max(0, after.BytesReceived - before.BytesReceived);
                up += Math.Max(0, after.BytesSent - before.BytesSent);
            }
            sample = new ThroughputSample(down / elapsed, up / elapsed);
        }

        // Sans écart de temps exploitable (horloge arrêtée), on garde la référence précédente.
        if (_previous is null || elapsed > 0)
        {
            _previous = current;
            _previousAt = now;
        }

        return Task.FromResult(new SourceReading(sample, new ModuleStatus(ModuleHealth.Online)));
    }

    public void Dispose()
    {
    }

    /// <summary>Vrai s'il y a au moins une vraie passerelle : 0.0.0.0 et :: (adresse non spécifiée) ne comptent pas.</summary>
    internal static bool HasUsableGateway(IEnumerable<IPAddress> gateways) =>
        gateways.Any(g =>
        {
            var address = g.IsIPv4MappedToIPv6 ? g.MapToIPv4() : g;
            return !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);
        });

    /// <summary>
    /// Compteurs des interfaces actives : en service, ni boucle locale ni tunnel, et avec au moins une passerelle
    /// (ce qui écarte les cartes virtuelles sans route vers l'extérieur ; une passerelle 0.0.0.0 ou :: ne compte pas).
    /// </summary>
    public static IReadOnlyList<InterfaceCounters> ReadSystemCounters()
    {
        var result = new List<InterfaceCounters>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
                    || !HasUsableGateway(nic.GetIPProperties().GatewayAddresses.Select(g => g.Address)))
                {
                    continue;
                }

                var statistics = nic.GetIPStatistics();
                result.Add(new InterfaceCounters(nic.Id, statistics.BytesReceived, statistics.BytesSent));
            }
            catch (NetworkInformationException)
            {
                // L'interface a disparu pendant l'énumération : elle n'est plus active.
            }
        }
        return result;
    }
}
