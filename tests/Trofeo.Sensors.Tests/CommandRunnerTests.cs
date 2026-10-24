namespace Trofeo.Sensors.Tests;

public class CommandRunnerTests
{
    private const string System32 = @"C:\Windows\System32";

    [Theory]
    [InlineData("schtasks", @"C:\Windows\System32\schtasks.exe")]
    [InlineData("schtasks.exe", @"C:\Windows\System32\schtasks.exe")]
    public void Resolve_BareName_GoesToTheSystemDirectory(string name, string expected) =>
        Assert.Equal(expected, ProcessCommandRunner.Resolve(name, System32));

    [Theory]
    [InlineData(@"C:\dl\PawnIO_setup.exe")]
    [InlineData(@"setup\PawnIO_setup.exe")]
    public void Resolve_NameWithDirectory_IsUnchanged(string path) =>
        Assert.Equal(path, ProcessCommandRunner.Resolve(path, System32));

    [Fact]
    public void Run_UnknownProgram_ThrowsInvalidOperation() =>
        Assert.Throws<InvalidOperationException>(() => new ProcessCommandRunner().Run(@"C:\does\not\exist.exe", ""));
}
