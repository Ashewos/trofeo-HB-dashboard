namespace Trofeo.Sensors;

/// <summary>Choisit, parmi les capteurs bruts, ceux qui alimentent les clés de <see cref="SensorKeys"/>. Code pur, sans accès au matériel.</summary>
public static class SensorMapper
{
    private const float MaxTemperature = 150f;

    private static readonly string[] CpuTempCandidates = ["CPU Package", "Core (Tctl/Tdie)", "Core Max", "Core Average"];

    /// <summary>
    /// Construit le relevé neutre. Le processeur est le premier matériel <c>Cpu</c> ; la carte graphique est la première
    /// NVIDIA, sinon AMD, sinon Intel. Si plusieurs matériels du même nom coexistent, leurs capteurs sont confondus.
    /// </summary>
    public static SensorSnapshot Map(IEnumerable<RawSensor> sensors, bool elevated)
    {
        var all = sensors as IReadOnlyList<RawSensor> ?? sensors.ToList();
        var values = new Dictionary<string, double>();

        var cpuName = all.FirstOrDefault(s => s.Hardware == RawHardware.Cpu)?.HardwareName;
        if (cpuName is not null)
            MapCpu(all.Where(s => s.Hardware == RawHardware.Cpu && s.HardwareName == cpuName).ToList(), values);

        string? gpuName = null;
        foreach (var family in new[] { RawHardware.GpuNvidia, RawHardware.GpuAmd, RawHardware.GpuIntel })
        {
            var first = all.FirstOrDefault(s => s.Hardware == family);
            if (first is null) continue;
            gpuName = first.HardwareName;
            MapGpu(all.Where(s => s.Hardware == family && s.HardwareName == gpuName).ToList(), values);
            break;
        }

        Set(values, SensorKeys.DiskTemp,
            Max(all.Where(s => s.Hardware == RawHardware.Storage && s.Kind == RawKind.Temperature), IsValidTemperature));

        return new SensorSnapshot(cpuName, gpuName, values, elevated);
    }

    private static void MapCpu(List<RawSensor> cpu, Dictionary<string, double> values)
    {
        Set(values, SensorKeys.CpuLoad, Find(cpu, RawKind.Load, "CPU Total", IsValid));

        double? temp = null;
        foreach (var name in CpuTempCandidates)
        {
            temp = Find(cpu, RawKind.Temperature, name, IsValidTemperature);
            if (temp is not null) break;
        }
        temp ??= Max(cpu.Where(s => s.Kind == RawKind.Temperature), IsValidTemperature);
        Set(values, SensorKeys.CpuTemp, temp);

        Set(values, SensorKeys.CpuClock,
            Max(cpu.Where(s => s.Kind == RawKind.Clock && !s.Name.Contains("Bus", StringComparison.Ordinal)), IsValid));
        Set(values, SensorKeys.CpuPower,
            First(cpu.Where(s => s.Kind == RawKind.Power && s.Name.Contains("Package", StringComparison.Ordinal)), IsValid));
    }

    private static void MapGpu(List<RawSensor> gpu, Dictionary<string, double> values)
    {
        Set(values, SensorKeys.GpuLoad, Find(gpu, RawKind.Load, "GPU Core", IsValid));
        Set(values, SensorKeys.GpuTemp, Find(gpu, RawKind.Temperature, "GPU Core", IsValidTemperature));
        Set(values, SensorKeys.GpuClock, Find(gpu, RawKind.Clock, "GPU Core", IsValid));
        Set(values, SensorKeys.GpuPower,
            Find(gpu, RawKind.Power, "GPU Package", IsValid) ?? First(gpu.Where(s => s.Kind == RawKind.Power), IsValid));
        Set(values, SensorKeys.GpuVramUsed, Find(gpu, RawKind.SmallData, "GPU Memory Used", IsValid));
        Set(values, SensorKeys.GpuVramTotal, Find(gpu, RawKind.SmallData, "GPU Memory Total", IsValid));
        Set(values, SensorKeys.GpuFan, First(gpu.Where(s => s.Kind == RawKind.Fan), IsValid));
    }

    private static void Set(Dictionary<string, double> values, string key, double? value)
    {
        if (value is { } v) values[key] = v;
    }

    private static double? Find(List<RawSensor> sensors, RawKind kind, string name, Func<float?, bool> valid) =>
        First(sensors.Where(s => s.Kind == kind && s.Name == name), valid);

    private static double? First(IEnumerable<RawSensor> sensors, Func<float?, bool> valid) =>
        sensors.Where(s => valid(s.Value)).Select(s => (double?)s.Value!.Value).FirstOrDefault();

    private static double? Max(IEnumerable<RawSensor> sensors, Func<float?, bool> valid) =>
        sensors.Where(s => valid(s.Value)).Select(s => (double?)s.Value!.Value).Max();

    /// <summary>Écarte <c>null</c>, NaN, infinis et négatifs. Un 0 reste valide (charge nulle, ventilateur à l'arrêt).</summary>
    private static bool IsValid(float? value) => value is { } v && float.IsFinite(v) && v >= 0f;

    /// <summary>Une température valide est aussi ≤ 150 °C et non nulle : un 0 exact est un capteur pas encore prêt.</summary>
    private static bool IsValidTemperature(float? value) => IsValid(value) && value > 0f && value <= MaxTemperature;
}
