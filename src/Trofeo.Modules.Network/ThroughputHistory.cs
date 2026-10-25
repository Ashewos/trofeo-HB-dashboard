namespace Trofeo.Modules.Network;

/// <summary>
/// Échantillons des dernières minutes, pour la courbe. Chaque ajout écarte ce qui date de plus de
/// <paramref name="window"/> avant lui. Sûr entre le minuteur du module et le thread d'affichage.
/// </summary>
public sealed class ThroughputHistory(TimeSpan window)
{
    private readonly object _gate = new();
    private readonly List<(DateTimeOffset At, ThroughputSample Sample)> _items = [];

    public void Add(DateTimeOffset at, ThroughputSample sample)
    {
        lock (_gate)
        {
            _items.Add((at, sample));
            var oldest = at - window;
            _items.RemoveAll(item => item.At < oldest);
        }
    }

    /// <summary>Copie des échantillons, du plus ancien au plus récent.</summary>
    public IReadOnlyList<(DateTimeOffset At, ThroughputSample Sample)> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }
}
