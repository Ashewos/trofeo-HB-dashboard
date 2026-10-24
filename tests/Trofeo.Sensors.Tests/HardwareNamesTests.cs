using Trofeo.Sensors;

namespace Trofeo.Sensors.Tests;

public class HardwareNamesTests
{
    [Theory]
    [InlineData("Intel Core Ultra 7 265K", "NVIDIA GeForce RTX 5070", "Core Ultra 7 265K · RTX 5070")]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "AMD Radeon RX 7800 XT", "Ryzen 7 7800X3D · RX 7800 XT")]
    [InlineData("Intel(R) Core(TM) i5-12400F", "Intel(R) Arc(TM) A770 Graphics", "Core i5-12400F · Arc A770")]
    public void Short_StripsVendorNoise(string cpu, string gpu, string expected) => Assert.Equal(expected, HardwareNames.Short(cpu, gpu));

    [Fact]
    public void Short_NoGpu() => Assert.Equal("Core i7-13700K", HardwareNames.Short("Intel Core i7-13700K", null));

    [Fact]
    public void Short_NoCpu() => Assert.Equal("RTX 5070", HardwareNames.Short(null, "NVIDIA GeForce RTX 5070"));

    [Fact]
    public void Short_Nothing() => Assert.Equal("Monitoring", HardwareNames.Short(null, null));

    [Fact]
    public void Short_RemovesWithRadeonGraphicsBeforeRadeon() =>
        Assert.Equal("Ryzen 5 5600G", HardwareNames.Short("AMD Ryzen 5 5600G with Radeon Graphics", null));

    [Fact]
    public void Short_EmptyAfterCleaning_IsSkipped() =>
        Assert.Equal("RTX 5070", HardwareNames.Short("   ", "NVIDIA GeForce RTX 5070"));
}
