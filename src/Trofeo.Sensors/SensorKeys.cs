namespace Trofeo.Sensors;

/// <summary>
/// Clés de <see cref="SensorSnapshot.Values"/>. Unités : charges et <c>disk.used</c> en %, températures en °C,
/// fréquences en MHz, puissances en W, VRAM en Mo, ventilateur en tr/min, RAM en Go (2^30 octets).
/// </summary>
public static class SensorKeys
{
    public const string CpuLoad = "cpu.load", CpuTemp = "cpu.temp", CpuClock = "cpu.clock", CpuPower = "cpu.power";
    public const string GpuLoad = "gpu.load", GpuTemp = "gpu.temp", GpuClock = "gpu.clock", GpuPower = "gpu.power";
    public const string GpuVramUsed = "gpu.vram.used", GpuVramTotal = "gpu.vram.total", GpuFan = "gpu.fan";
    public const string RamUsed = "ram.used", RamTotal = "ram.total", DiskTemp = "disk.temp", DiskUsed = "disk.used";
}
