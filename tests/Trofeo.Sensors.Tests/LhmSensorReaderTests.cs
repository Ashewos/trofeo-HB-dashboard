using Trofeo.Sensors;

namespace Trofeo.Sensors.Tests;

public class LhmSensorReaderTests
{
    /// <summary>Test de fumée : lit le matériel réel de la machine, sans droits administrateur.</summary>
    [Fact]
    public void LhmSensorReader_Read_ReturnsRamAndCpuLoad()
    {
        using var reader = new LhmSensorReader();

        var snap = reader.Read();

        Assert.True(snap.Values[SensorKeys.RamTotal] > 0);
        Assert.True(snap.Values[SensorKeys.RamUsed] > 0);
        Assert.True(snap.Values.ContainsKey(SensorKeys.CpuLoad));
        Assert.False(string.IsNullOrWhiteSpace(snap.CpuName));
    }
}
