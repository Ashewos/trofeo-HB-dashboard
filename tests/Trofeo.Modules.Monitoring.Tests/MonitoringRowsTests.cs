using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Sensors;

namespace Trofeo.Modules.Monitoring.Tests;

public class MonitoringRowsTests
{
    private static SensorSnapshot Snapshot(params (string Key, double Value)[] values) =>
        new("CPU", "GPU", values.ToDictionary(v => v.Key, v => v.Value), Elevated: true);

    private static SKRect Rect(int col, int row, int cols, int rows)
    {
        var r = GridGeometry.ModuleRect(col, row, cols, rows);
        return new SKRect(r.Left, r.Top, r.Right, r.Bottom);
    }

    [Fact]
    public void Build_Cpu_FormatsLoadAndTemp()
    {
        var row = MonitoringRows.Build("cpu", Snapshot((SensorKeys.CpuLoad, 34.4), (SensorKeys.CpuTemp, 52.2)));

        Assert.Equal("CPU", row.Label);
        Assert.Equal("34 %", row.Value);
        Assert.Equal("52 °C", row.Secondary);
        Assert.Equal(0.344f, row.Bar!.Value, 3);
    }

    [Fact]
    public void Build_Ram_FormatsUsedAndTotal()
    {
        var row = MonitoringRows.Build("ram", Snapshot((SensorKeys.RamUsed, 18.24), (SensorKeys.RamTotal, 31.8)));

        Assert.Equal("RAM", row.Label);
        Assert.Equal("18,2", row.Value);
        Assert.Equal("/ 32 Go", row.Secondary);
        Assert.Equal(0.574f, row.Bar!.Value, 3);
    }

    [Fact]
    public void Build_Vram_ConvertsMegabytesToGigabytes()
    {
        var row = MonitoringRows.Build("vram", Snapshot((SensorKeys.GpuVramUsed, 5222), (SensorKeys.GpuVramTotal, 12288)));

        Assert.Equal("VRAM", row.Label);
        Assert.Equal("5,1", row.Value);
        Assert.Equal("/ 12 Go", row.Secondary);
        Assert.Equal(5222f / 12288f, row.Bar!.Value, 3);
    }

    [Fact]
    public void Build_ClockPowerFanDisk_FormatsEachColumn()
    {
        var s = Snapshot(
            (SensorKeys.CpuClock, 5449), (SensorKeys.GpuClock, 2805),
            (SensorKeys.CpuPower, 124.5), (SensorKeys.GpuPower, 180.4),
            (SensorKeys.GpuFan, 1450), (SensorKeys.DiskUsed, 62.5), (SensorKeys.DiskTemp, 38));

        var clock = MonitoringRows.Build("clock", s);
        Assert.Equal(("Fréq.", null, "5,4 GHz", "2,8 GHz"), (clock.Label, clock.Bar, clock.Value, clock.Secondary));

        var power = MonitoringRows.Build("power", s);
        Assert.Equal(("Conso", null, "125 W", "180 W"), (power.Label, power.Bar, power.Value, power.Secondary));

        var fan = MonitoringRows.Build("fan", s);
        Assert.Equal(("Ventil.", null, "1450 tr/min", null), (fan.Label, fan.Bar, fan.Value, fan.Secondary));

        var disk = MonitoringRows.Build("disk", s);
        Assert.Equal("Disque", disk.Label);
        Assert.Equal("63 %", disk.Value);
        Assert.Equal("38 °C", disk.Secondary);
        Assert.Equal(0.625f, disk.Bar!.Value, 3);
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("gpu")]
    [InlineData("ram")]
    [InlineData("vram")]
    [InlineData("clock")]
    [InlineData("power")]
    [InlineData("fan")]
    [InlineData("disk")]
    public void Build_MissingValues_ShowDash(string id)
    {
        foreach (var snapshot in new[] { null, Snapshot() })
        {
            var row = MonitoringRows.Build(id, snapshot);

            Assert.Equal(MonitoringRows.Dash, row.Value);
            Assert.Null(row.Bar);
            Assert.True(row.Secondary is null or MonitoringRows.Dash, row.Secondary);
        }
    }

    [Fact]
    public void Build_BarIsClampedAndTotalZeroGivesNoBar()
    {
        Assert.Equal(1f, MonitoringRows.Build("cpu", Snapshot((SensorKeys.CpuLoad, 140))).Bar);
        Assert.Null(MonitoringRows.Build("ram", Snapshot((SensorKeys.RamUsed, 4), (SensorKeys.RamTotal, 0))).Bar);
    }

    [Fact]
    public void Build_UnknownRow_Throws() =>
        Assert.Throws<ArgumentException>(() => MonitoringRows.Build("nope", null));

    [Fact]
    public void Capacity_Compact4x2_IsThree() =>
        Assert.Equal(3, MonitoringRows.Capacity(Rect(8, 0, 4, 2), 1f));

    [Fact]
    public void Capacity_Large6x4_IsAtLeastSix() =>
        Assert.Equal(7, MonitoringRows.Capacity(Rect(0, 0, 6, 4), 1f));

    [Fact]
    public void Capacity_ShrinksWithTextScale_AndNeverNegative()
    {
        var compact = Rect(8, 0, 4, 2);
        Assert.Equal(3, MonitoringRows.Capacity(compact, 0.85f));
        Assert.Equal(2, MonitoringRows.Capacity(compact, 1.2f));
        Assert.Equal(0, MonitoringRows.Capacity(new SKRect(0, 0, 300, 30), 1f));
    }
}
