using LibreHardwareMonitor.Hardware;

namespace Trofeo.Sensors;

/// <summary>Source de capteurs bruts ouverte puis lue par <see cref="LhmSensorReader"/>. Séparée pour pouvoir simuler l'ouverture dans les tests.</summary>
internal interface IHardwareSource : IDisposable
{
    /// <summary>Ouvre la source. En cas d'échec, lève une exception et ne laisse rien d'ouvert.</summary>
    void Open();

    /// <summary>Met à jour le matériel et retourne tous ses capteurs, sous-matériels compris.</summary>
    IReadOnlyList<RawSensor> ReadRaw();
}

/// <summary>Implémentation réelle, au-dessus de LibreHardwareMonitor.</summary>
internal sealed class LhmHardwareSource : IHardwareSource
{
    private static readonly UpdateVisitor Updater = new();

    private Computer? _computer;

    public void Open()
    {
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true };
        try
        {
            computer.Open();
        }
        catch
        {
            // Open a pu s'arrêter à mi-chemin : on libère ce qui a été acquis avant de signaler l'échec.
            TryClose(computer);
            throw;
        }
        _computer = computer;
    }

    public IReadOnlyList<RawSensor> ReadRaw()
    {
        var computer = _computer ?? throw new InvalidOperationException("Source non ouverte.");
        try { computer.Accept(Updater); }
        catch { /* on convertit ce qui a pu être mis à jour */ }

        var raw = new List<RawSensor>();
        foreach (var hardware in computer.Hardware)
            Collect(hardware, raw);
        return raw;
    }

    public void Dispose()
    {
        if (_computer is { } computer) TryClose(computer);
        _computer = null;
    }

    private static void TryClose(Computer computer)
    {
        try { computer.Close(); }
        catch { /* le matériel est de toute façon libéré à la sortie du processus */ }
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
                // Un capteur défaillant est ignoré sans bruit : il est lu chaque seconde, donc une trace par
                // échec inonderait le journal, et sa clé absente du relevé suffit à le signaler.
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
