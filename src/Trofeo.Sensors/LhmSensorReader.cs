using System.Diagnostics;

namespace Trofeo.Sensors;

/// <summary>
/// Lecteur de capteurs fondé sur LibreHardwareMonitor. Ajoute aux valeurs mappées la RAM
/// (<c>GlobalMemoryStatusEx</c>) et le taux d'occupation du disque système (<see cref="DriveInfo"/>).
/// <para>
/// Ouverture paresseuse : LibreHardwareMonitor est ouvert au premier <see cref="Read"/> et refermé par
/// <see cref="Dispose"/>. Si l'ouverture échoue (pilote indisponible…), l'échec est signalé par
/// <see cref="Trace.TraceWarning(string)"/>, ce qui a été acquis est libéré, et l'ouverture est retentée au plus une
/// fois tous les <see cref="RetryIntervalReads"/> appels à <see cref="Read"/>. Entre-temps, les relevés ne
/// contiennent que la RAM et le disque, et <see cref="IsOpen"/> vaut <c>false</c>.
/// </para>
/// <para>
/// Fil d'exécution : <see cref="Read"/> est appelé au plus une fois par seconde et jamais en parallèle (le hub
/// le garantit) ; la classe n'a donc aucun verrou interne.
/// </para>
/// </summary>
public sealed class LhmSensorReader : ISensorReader
{
    /// <summary>Nombre d'appels à <see cref="Read"/> entre deux tentatives d'ouverture après un échec.</summary>
    internal const int RetryIntervalReads = 30;

    private readonly Func<IHardwareSource> _createSource;
    private IHardwareSource? _source;
    private int _readsBeforeRetry;
    private bool _disposed;

    public LhmSensorReader() : this(static () => new LhmHardwareSource())
    {
    }

    internal LhmSensorReader(Func<IHardwareSource> createSource) => _createSource = createSource;

    /// <summary>Vrai tant que LibreHardwareMonitor est ouvert. Faux avant le premier <see cref="Read"/>, après un échec d'ouverture et après <see cref="Dispose"/>.</summary>
    public bool IsOpen => _source is not null;

    public SensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureOpen();

        var elevated = Environment.IsPrivilegedProcess;
        var snapshot = ReadMapped(elevated);

        var values = new Dictionary<string, double>(snapshot.Values);
        if (MemoryStatus.TryRead(out var used, out var total))
        {
            values[SensorKeys.RamUsed] = used;
            values[SensorKeys.RamTotal] = total;
        }
        if (TryReadSystemDiskUsage(out var diskUsed))
            values[SensorKeys.DiskUsed] = diskUsed;

        return snapshot with { Values = values };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeSource();
    }

    private SensorSnapshot ReadMapped(bool elevated)
    {
        if (_source is { } source)
        {
            try
            {
                return SensorMapper.Map(source.ReadRaw(), elevated);
            }
            catch (Exception e)
            {
                Trace.TraceWarning($"Lecture des capteurs impossible : {e.Message}");
            }
        }
        return new SensorSnapshot(null, null, new Dictionary<string, double>(), elevated);
    }

    private void EnsureOpen()
    {
        if (_source is not null) return;
        if (_readsBeforeRetry > 0)
        {
            _readsBeforeRetry--;
            return;
        }

        IHardwareSource? source = null;
        try
        {
            source = _createSource();
            source.Open();
            _source = source;
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Ouverture des capteurs impossible, nouvel essai dans {RetryIntervalReads} lectures : {e.Message}");
            try { source?.Dispose(); }
            catch { /* déjà en échec : rien de plus à faire */ }
            _readsBeforeRetry = RetryIntervalReads - 1;
        }
    }

    private void DisposeSource()
    {
        try { _source?.Dispose(); }
        catch { /* le matériel est de toute façon libéré à la sortie du processus */ }
        _source = null;
    }

    private static bool TryReadSystemDiskUsage(out double percent)
    {
        percent = 0;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            if (drive.TotalSize <= 0) return false;
            percent = (drive.TotalSize - drive.TotalFreeSpace) / (double)drive.TotalSize * 100.0;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
