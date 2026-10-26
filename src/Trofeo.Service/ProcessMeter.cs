using Trofeo.Control;

namespace Trofeo.Display;

/// <summary>
/// Mesure du processus pour la fenêtre de réglage : mémoire et CPU. Le CPU est la moyenne depuis la lecture précédente,
/// en pourcentage de la machine entière (un cœur à fond sur 4 cœurs : 25 %) ; la première lecture donne 0.
/// </summary>
/// <param name="sample">Temps CPU cumulé du processus, mémoire privée et mémoire de travail, en octets.</param>
public sealed class ProcessMeter(Func<(TimeSpan Cpu, long PrivateBytes, long WorkingSet)> sample, TimeProvider time, int processorCount)
{
    private readonly object _gate = new();
    private long _lastTimestamp;
    private TimeSpan _lastCpu;
    private bool _hasPrevious;
    private double _cpuPercent;

    public ProcessInfo Read()
    {
        lock (_gate)
        {
            // La mesure est prise sous le verrou, avec l'horodatage : deux lecteurs en même temps ne font pas reculer _lastCpu.
            var (cpu, privateBytes, workingSet) = sample();
            var now = time.GetTimestamp();
            if (_hasPrevious)
            {
                var wall = time.GetElapsedTime(_lastTimestamp, now);
                // Deux lectures au même instant : on garde la valeur précédente plutôt que de diviser par zéro.
                if (wall > TimeSpan.Zero)
                {
                    var percent = (cpu - _lastCpu).TotalSeconds / (wall.TotalSeconds * Math.Max(1, processorCount)) * 100;
                    _cpuPercent = Math.Clamp(percent, 0, 100);
                    _lastTimestamp = now;
                    _lastCpu = cpu;
                }
            }
            else
            {
                _hasPrevious = true;
                _lastTimestamp = now;
                _lastCpu = cpu;
            }

            return new ProcessInfo(privateBytes, workingSet, _cpuPercent);
        }
    }
}
