using System.Net.NetworkInformation;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>Compteurs d'octets cumulés d'une interface réseau.</summary>
public sealed record InterfaceCounters(string Id, long BytesReceived, long BytesSent);

/// <summary>
/// Débit des cartes réseau du PC : écart des compteurs cumulés entre deux lectures, divisé par le temps écoulé.
/// Appelée par le module sans chevauchement, donc sans verrou.
/// </summary>
public sealed class NicSource(Func<IReadOnlyList<InterfaceCounters>> readCounters, TimeProvider time) : IThroughputSource
{
    private Dictionary<string, InterfaceCounters>? _previous;
    private DateTimeOffset _previousAt;

    public string Title => "Ce PC";

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public Task<SourceReading> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var counters = readCounters();
        var now = time.GetUtcNow();
        var current = new Dictionary<string, InterfaceCounters>(counters.Count);
        foreach (var item in counters) current[item.Id] = item;

        ThroughputSample? sample = null;
        var elapsed = (now - _previousAt).TotalSeconds;
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

        // Sans écart de temps exploitable (horloge arrêtée ou reculée), on garde la référence précédente.
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

    /// <summary>
    /// Compteurs des interfaces actives : en service, ni boucle locale ni tunnel, et avec au moins une passerelle
    /// (ce qui écarte les cartes virtuelles sans route vers l'extérieur).
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
                    || nic.GetIPProperties().GatewayAddresses.Count == 0)
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
