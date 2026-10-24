using LibreHardwareMonitor.Hardware;

namespace Trofeo.Sensors;

/// <summary>
/// Lecteur de capteurs fondé sur LibreHardwareMonitor. Ajoute aux valeurs mappées la RAM
/// (<c>GlobalMemoryStatusEx</c>) et le taux d'occupation du disque système (<see cref="DriveInfo"/>).
/// <para>
/// Ouverture paresseuse : LibreHardwareMonitor est ouvert une seule fois, au premier <see cref="Read"/>, et refermé
/// par <see cref="Dispose"/>. Si l'ouverture échoue (pilote indisponible…), elle n'est pas retentée : les relevés
/// suivants ne contiennent que la RAM et le disque.
/// </para>
/// <para>
/// Fil d'exécution : <see cref="Read"/> est appelé au plus une fois par seconde et jamais en parallèle (le hub
/// le garantit) ; la classe n'a donc aucun verrou interne.
/// </para>
/// </summary>
public sealed class LhmSensorReader : ISensorReader
{
    private static readonly UpdateVisitor Updater = new();

    private Computer? _computer;
    private bool _opened;
    private bool _disposed;

    public SensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OpenOnce();

        var elevated = Environment.IsPrivilegedProcess;
        var snapshot = _computer is { } computer
            ? SensorMapper.Map(ReadRaw(computer), elevated)
            : new SensorSnapshot(null, null, new Dictionary<string, double>(), elevated);

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
        try { _computer?.Close(); }
        catch { /* le matériel est de toute façon libéré à la sortie du processus */ }
        _computer = null;
    }

    private void OpenOnce()
    {
        if (_opened) return;
        _opened = true;
        try
        {
            var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true };
            computer.Open();
            _computer = computer;
        }
        catch
        {
            _computer = null;
        }
    }

    private static List<RawSensor> ReadRaw(Computer computer)
    {
        try { computer.Accept(Updater); }
        catch { /* on convertit ce qui a pu être mis à jour */ }

        var raw = new List<RawSensor>();
        foreach (var hardware in computer.Hardware)
            Collect(hardware, raw);
        return raw;
    }

    private static void Collect(IHardware hardware, List<RawSensor> raw)
    {
        var type = ToRaw(hardware.HardwareType);
        foreach (var sensor in hardware.Sensors)
        {
            try
            {
                raw.Add(new RawSensor(type, hardware.Name, ToRaw(sensor.SensorType), sensor.Name, sensor.Value));
            }
            catch
            {
                // un capteur défaillant n'empêche pas la lecture des autres
            }
        }
        foreach (var sub in hardware.SubHardware)
            Collect(sub, raw);
    }

    private static RawHardware ToRaw(HardwareType type) => type switch
    {
        HardwareType.Cpu => RawHardware.Cpu,
        HardwareType.GpuNvidia => RawHardware.GpuNvidia,
        HardwareType.GpuAmd => RawHardware.GpuAmd,
        HardwareType.GpuIntel => RawHardware.GpuIntel,
        HardwareType.Storage => RawHardware.Storage,
        _ => RawHardware.Other,
    };

    private static RawKind ToRaw(SensorType type) => type switch
    {
        SensorType.Load => RawKind.Load,
        SensorType.Temperature => RawKind.Temperature,
        SensorType.Clock => RawKind.Clock,
        SensorType.Power => RawKind.Power,
        SensorType.SmallData => RawKind.SmallData,
        SensorType.Fan => RawKind.Fan,
        _ => RawKind.Other,
    };

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

    /// <summary>Met à jour chaque matériel ; un matériel qui échoue n'empêche pas les autres.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            try { hardware.Update(); }
            catch { /* ses capteurs garderont leur dernière valeur ou null */ }
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
