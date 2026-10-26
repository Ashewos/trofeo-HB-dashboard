using Trofeo.Sensors;

namespace Trofeo.Sensors.Tests;

public class SensorMapperTests
{
    private const string IntelCpu = "Intel Core Ultra 7 265K";
    private const string NvidiaGpu = "NVIDIA GeForce RTX 5070";

    private static RawSensor Cpu(RawKind kind, string name, float? value, string hw = IntelCpu) => new(RawHardware.Cpu, hw, kind, name, value);

    private static RawSensor Gpu(RawKind kind, string name, float? value, RawHardware hw = RawHardware.GpuNvidia, string hwName = NvidiaGpu) =>
        new(hw, hwName, kind, name, value);

    private static List<RawSensor> RealisticIntelNvidia() =>
    [
        Cpu(RawKind.Load, "CPU Total", 34),
        Cpu(RawKind.Temperature, "CPU Package", 52),
        Cpu(RawKind.Clock, "P-Core #1", 5400),
        Cpu(RawKind.Clock, "Bus Speed", 100),
        Cpu(RawKind.Power, "CPU Package", 125),
        Gpu(RawKind.Load, "GPU Core", 61),
        Gpu(RawKind.Temperature, "GPU Core", 64),
        Gpu(RawKind.Clock, "GPU Core", 2800),
        Gpu(RawKind.Power, "GPU Package", 180),
        Gpu(RawKind.SmallData, "GPU Memory Used", 5120),
        Gpu(RawKind.SmallData, "GPU Memory Total", 12288),
        Gpu(RawKind.Fan, "GPU Fan", 1450),
    ];

    [Fact]
    public void Map_IntelNvidia()
    {
        var snap = SensorMapper.Map(RealisticIntelNvidia(), elevated: false);

        Assert.Equal(IntelCpu, snap.CpuName);
        Assert.Equal(NvidiaGpu, snap.GpuName);
        Assert.False(snap.Elevated);
        Assert.Equal(34, snap.Values[SensorKeys.CpuLoad]);
        Assert.Equal(52, snap.Values[SensorKeys.CpuTemp]);
        Assert.Equal(5400, snap.Values[SensorKeys.CpuClock]);
        Assert.Equal(125, snap.Values[SensorKeys.CpuPower]);
        Assert.Equal(61, snap.Values[SensorKeys.GpuLoad]);
        Assert.Equal(64, snap.Values[SensorKeys.GpuTemp]);
        Assert.Equal(2800, snap.Values[SensorKeys.GpuClock]);
        Assert.Equal(180, snap.Values[SensorKeys.GpuPower]);
        Assert.Equal(5120, snap.Values[SensorKeys.GpuVramUsed]);
        Assert.Equal(12288, snap.Values[SensorKeys.GpuVramTotal]);
        Assert.Equal(1450, snap.Values[SensorKeys.GpuFan]);
        Assert.Equal(11, snap.Values.Count);
    }

    [Fact]
    public void Map_PassesElevatedFlag() =>
        Assert.True(SensorMapper.Map(RealisticIntelNvidia(), elevated: true).Elevated);

    [Fact]
    public void Map_AmdCpuAndGpu()
    {
        List<RawSensor> sensors =
        [
            Cpu(RawKind.Load, "CPU Total", 10, "AMD Ryzen 7 7800X3D 8-Core Processor"),
            Cpu(RawKind.Temperature, "Core (Tctl/Tdie)", 71, "AMD Ryzen 7 7800X3D 8-Core Processor"),
            Gpu(RawKind.Load, "GPU Core", 5, RawHardware.GpuIntel, "Intel(R) UHD Graphics"),
            Gpu(RawKind.Temperature, "GPU Core", 40, RawHardware.GpuIntel, "Intel(R) UHD Graphics"),
            Gpu(RawKind.Load, "GPU Core", 88, RawHardware.GpuAmd, "AMD Radeon RX 7800 XT"),
            Gpu(RawKind.Temperature, "GPU Core", 66, RawHardware.GpuAmd, "AMD Radeon RX 7800 XT"),
        ];

        var snap = SensorMapper.Map(sensors, elevated: true);

        Assert.Equal(71, snap.Values[SensorKeys.CpuTemp]);
        Assert.Equal("AMD Radeon RX 7800 XT", snap.GpuName);
        Assert.Equal(88, snap.Values[SensorKeys.GpuLoad]);
        Assert.Equal(66, snap.Values[SensorKeys.GpuTemp]);
    }

    [Fact]
    public void Map_NvidiaPreferredOverAmd()
    {
        List<RawSensor> sensors =
        [
            Gpu(RawKind.Load, "GPU Core", 10, RawHardware.GpuAmd, "AMD Radeon(TM) Graphics"),
            Gpu(RawKind.Load, "GPU Core", 90),
        ];

        var snap = SensorMapper.Map(sensors, elevated: false);

        Assert.Equal(NvidiaGpu, snap.GpuName);
        Assert.Equal(90, snap.Values[SensorKeys.GpuLoad]);
    }

    [Fact]
    public void Map_AmdIgpuAndAmdDgpu_PicksDiscrete()
    {
        List<RawSensor> sensors =
        [
            Gpu(RawKind.Load, "GPU Core", 3, RawHardware.GpuAmd, "AMD Radeon(TM) Graphics"),
            Gpu(RawKind.SmallData, "GPU Memory Total", 512, RawHardware.GpuAmd, "AMD Radeon(TM) Graphics"),
            Gpu(RawKind.Load, "GPU Core", 77, RawHardware.GpuAmd, "AMD Radeon RX 7800 XT"),
            Gpu(RawKind.SmallData, "GPU Memory Total", 16384, RawHardware.GpuAmd, "AMD Radeon RX 7800 XT"),
        ];

        var snap = SensorMapper.Map(sensors, elevated: false);

        Assert.Equal("AMD Radeon RX 7800 XT", snap.GpuName);
        Assert.Equal(77, snap.Values[SensorKeys.GpuLoad]);
        Assert.Equal(16384, snap.Values[SensorKeys.GpuVramTotal]);
    }

    [Fact]
    public void Map_AmdIgpuAndIntelArc_PicksArc()
    {
        List<RawSensor> sensors =
        [
            Gpu(RawKind.Load, "GPU Core", 3, RawHardware.GpuAmd, "AMD Radeon(TM) Graphics"),
            Gpu(RawKind.SmallData, "GPU Memory Total", 512, RawHardware.GpuAmd, "AMD Radeon(TM) Graphics"),
            Gpu(RawKind.Load, "GPU Core", 40, RawHardware.GpuIntel, "Intel(R) Arc(TM) A770 Graphics"),
            Gpu(RawKind.SmallData, "GPU Memory Total", 16384, RawHardware.GpuIntel, "Intel(R) Arc(TM) A770 Graphics"),
        ];

        var snap = SensorMapper.Map(sensors, elevated: false);

        Assert.Equal("Intel(R) Arc(TM) A770 Graphics", snap.GpuName);
        Assert.Equal(40, snap.Values[SensorKeys.GpuLoad]);
    }

    [Fact]
    public void Map_EqualGpuMemory_PrefersNvidiaThenAmdThenIntel()
    {
        List<RawSensor> sensors =
        [
            Gpu(RawKind.SmallData, "GPU Memory Total", 8192, RawHardware.GpuIntel, "Intel Arc"),
            Gpu(RawKind.SmallData, "GPU Memory Total", 8192, RawHardware.GpuAmd, "AMD Radeon RX"),
            Gpu(RawKind.SmallData, "GPU Memory Total", 8192),
        ];

        Assert.Equal(NvidiaGpu, SensorMapper.Map(sensors, elevated: false).GpuName);
        Assert.Equal("AMD Radeon RX", SensorMapper.Map(sensors.Take(2), elevated: false).GpuName);
    }

    [Fact]
    public void Map_InvalidGpuMemoryTotal_IsIgnoredForChoice()
    {
        List<RawSensor> sensors =
        [
            Gpu(RawKind.SmallData, "GPU Memory Total", float.NaN, RawHardware.GpuIntel, "Intel Arc"),
            Gpu(RawKind.Load, "GPU Core", 1),
        ];

        Assert.Equal(NvidiaGpu, SensorMapper.Map(sensors, elevated: false).GpuName);
    }

    [Fact]
    public void Map_NoGpu()
    {
        var snap = SensorMapper.Map([Cpu(RawKind.Load, "CPU Total", 12)], elevated: false);

        Assert.Null(snap.GpuName);
        Assert.DoesNotContain(snap.Values.Keys, k => k.StartsWith("gpu.", StringComparison.Ordinal));
        Assert.Equal(12, snap.Values[SensorKeys.CpuLoad]);
    }

    [Fact]
    public void Map_NothingAtAll()
    {
        var snap = SensorMapper.Map([], elevated: false);

        Assert.Null(snap.CpuName);
        Assert.Null(snap.GpuName);
        Assert.Empty(snap.Values);
    }

    [Fact]
    public void Map_DiscardsInvalidValues()
    {
        List<RawSensor> sensors =
        [
            Cpu(RawKind.Load, "CPU Total", float.NaN),
            Cpu(RawKind.Temperature, "CPU Package", 200),
            Cpu(RawKind.Clock, "P-Core #1", -5),
            Cpu(RawKind.Power, "CPU Package", float.PositiveInfinity),
            Gpu(RawKind.Load, "GPU Core", null),
            Gpu(RawKind.Temperature, "GPU Core", 0),
        ];

        var snap = SensorMapper.Map(sensors, elevated: false);

        Assert.Empty(snap.Values);
        Assert.Equal(IntelCpu, snap.CpuName);
    }

    [Fact]
    public void Map_ZeroLoadIsKept() =>
        Assert.Equal(0, SensorMapper.Map([Cpu(RawKind.Load, "CPU Total", 0)], elevated: false).Values[SensorKeys.CpuLoad]);

    [Fact]
    public void Map_InvalidPackageTemperature_FallsBackToNextCandidate()
    {
        List<RawSensor> sensors =
        [
            Cpu(RawKind.Temperature, "CPU Package", 200),
            Cpu(RawKind.Temperature, "Core Max", 61),
        ];

        Assert.Equal(61, SensorMapper.Map(sensors, elevated: false).Values[SensorKeys.CpuTemp]);
    }

    [Fact]
    public void Map_NoPackage_FallbackIgnoresDistanceToTjMaxSensors()
    {
        List<RawSensor> sensors =
        [
            Cpu(RawKind.Temperature, "Core #1", 48),
            Cpu(RawKind.Temperature, "Core #2", 57),
            Cpu(RawKind.Temperature, "Core #1 Distance to TjMax", 85), // marge restante avant la coupure, pas une température
            Cpu(RawKind.Temperature, "Core #2 Distance to TjMax", 43),
        ];

        Assert.Equal(57, SensorMapper.Map(sensors, elevated: false).Values[SensorKeys.CpuTemp]);
    }

    [Fact]
    public void Map_OnlyDistanceSensors_HasNoCpuTemperature()
    {
        List<RawSensor> sensors = [Cpu(RawKind.Temperature, "Core #1 Distance to TjMax", 52)];

        Assert.False(SensorMapper.Map(sensors, elevated: false).Values.ContainsKey(SensorKeys.CpuTemp));
    }

    [Fact]
    public void Map_NoPackage_UsesMaxCoreTemperature()
    {
        List<RawSensor> sensors =
        [
            Cpu(RawKind.Temperature, "Core #1", 48),
            Cpu(RawKind.Temperature, "Core #2", 57),
            Cpu(RawKind.Temperature, "Core #3", 51),
        ];

        Assert.Equal(57, SensorMapper.Map(sensors, elevated: false).Values[SensorKeys.CpuTemp]);
    }

    [Fact]
    public void Map_CpuClock_IgnoresBusSpeed()
    {
        List<RawSensor> sensors =
        [
            Cpu(RawKind.Clock, "Bus Speed", 100),
            Cpu(RawKind.Clock, "Core #1", 3200),
            Cpu(RawKind.Clock, "Core #2", 4100),
        ];

        Assert.Equal(4100, SensorMapper.Map(sensors, elevated: false).Values[SensorKeys.CpuClock]);
    }

    [Fact]
    public void Map_GpuPower_FallsBackToFirstPower()
    {
        List<RawSensor> sensors =
        [
            Gpu(RawKind.Power, "GPU Core", 90),
            Gpu(RawKind.Power, "GPU SRAM", 5),
        ];

        Assert.Equal(90, SensorMapper.Map(sensors, elevated: false).Values[SensorKeys.GpuPower]);
    }

    [Fact]
    public void Map_DiskTemp_IsMaxOverStorage()
    {
        List<RawSensor> sensors =
        [
            new(RawHardware.Storage, "Samsung 990 PRO", RawKind.Temperature, "Temperature", 42),
            new(RawHardware.Storage, "WD Blue", RawKind.Temperature, "Temperature", 47),
            new(RawHardware.Storage, "WD Blue", RawKind.Temperature, "Temperature 2", float.NaN),
        ];

        Assert.Equal(47, SensorMapper.Map(sensors, elevated: false).Values[SensorKeys.DiskTemp]);
    }

    [Fact]
    public void Map_IgnoresOtherHardware()
    {
        List<RawSensor> sensors = [new(RawHardware.Other, "Mainboard", RawKind.Temperature, "CPU Package", 40)];

        Assert.Empty(SensorMapper.Map(sensors, elevated: false).Values);
    }
    private const string IntelGpu = "Intel(R) Graphics";

    private static List<RawSensor> TwoGpus() =>
    [
        Cpu(RawKind.Load, "CPU Total", 34),
        // Listée d'abord : l'iGPU ne doit pas passer devant la carte dédiée.
        Gpu(RawKind.Load, "GPU Core", 7, RawHardware.GpuIntel, IntelGpu),
        Gpu(RawKind.SmallData, "GPU Memory Total", 128, RawHardware.GpuIntel, IntelGpu),
        Gpu(RawKind.Load, "GPU Core", 61),
        Gpu(RawKind.Temperature, "GPU Core", 64),
        Gpu(RawKind.SmallData, "GPU Memory Total", 12288),
    ];

    [Fact]
    public void Map_TwoGpus_ListsBothInRankOrder()
    {
        var snap = SensorMapper.Map(TwoGpus(), elevated: false);

        Assert.NotNull(snap.Gpus);
        Assert.Equal([NvidiaGpu, IntelGpu], snap.Gpus.Select(g => g.Name));
        Assert.Equal(61, snap.Gpus[0].Values[SensorKeys.GpuLoad]);
        Assert.Equal(64, snap.Gpus[0].Values[SensorKeys.GpuTemp]);
        Assert.Equal(12288, snap.Gpus[0].Values[SensorKeys.GpuVramTotal]);
        Assert.Equal(7, snap.Gpus[1].Values[SensorKeys.GpuLoad]);
        Assert.False(snap.Gpus[1].Values.ContainsKey(SensorKeys.GpuTemp));
        // La carte retenue reste dans Values, comme avant.
        Assert.Equal(NvidiaGpu, snap.GpuName);
        Assert.Equal(61, snap.Values[SensorKeys.GpuLoad]);
    }

    [Fact]
    public void Map_NoGpu_GivesEmptyList()
    {
        var snap = SensorMapper.Map([Cpu(RawKind.Load, "CPU Total", 34)], elevated: false);

        Assert.NotNull(snap.Gpus);
        Assert.Empty(snap.Gpus);
    }

    [Fact]
    public void ForGpu_SwapsGpuValuesAndName()
    {
        var snap = SensorMapper.Map(TwoGpus(), elevated: false);

        var intel = snap.ForGpu(IntelGpu);

        Assert.Equal(IntelGpu, intel.GpuName);
        Assert.Equal(7, intel.Values[SensorKeys.GpuLoad]);
        Assert.Equal(128, intel.Values[SensorKeys.GpuVramTotal]);
        // La température de la NVIDIA ne doit pas rester sur la ligne de l'Intel.
        Assert.False(intel.Values.ContainsKey(SensorKeys.GpuTemp));
        Assert.Equal(34, intel.Values[SensorKeys.CpuLoad]);
        Assert.Equal(snap.CpuName, intel.CpuName);
        Assert.Equal(snap.Elevated, intel.Elevated);
        Assert.Same(snap.Gpus, intel.Gpus);
        // Le relevé d'origine n'est pas modifié.
        Assert.Equal(61, snap.Values[SensorKeys.GpuLoad]);
    }

    [Fact]
    public void ForGpu_UnknownOrNull_ReturnsSame()
    {
        var snap = SensorMapper.Map(TwoGpus(), elevated: false);

        Assert.Same(snap, snap.ForGpu(null));
        Assert.Same(snap, snap.ForGpu("Carte inconnue"));
        Assert.Same(snap, snap.ForGpu(NvidiaGpu.ToLowerInvariant())); // la casse compte
        var old = new SensorSnapshot("CPU", "GPU", new Dictionary<string, double>(), Elevated: false);
        Assert.Same(old, old.ForGpu("GPU"));
    }
}
