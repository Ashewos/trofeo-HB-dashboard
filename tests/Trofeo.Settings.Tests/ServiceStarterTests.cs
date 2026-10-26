using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

public class ServiceStarterTests
{
    [Fact]
    public void Start_NotFound_ReturnsFalseWithoutLaunching()
    {
        var launched = false;
        var starter = new ServiceStarter(() => null, _ => { launched = true; return true; });

        Assert.False(starter.TryStart());
        Assert.False(launched);
    }

    [Fact]
    public void Start_Found_LaunchesThatPath()
    {
        string? launched = null;
        var starter = new ServiceStarter(() => "/x/Trofeo.Display.exe", path => { launched = path; return true; });

        Assert.True(starter.TryStart());
        Assert.Equal("/x/Trofeo.Display.exe", launched);
    }

    [Fact]
    public void Start_LaunchFails_ReturnsFalse()
    {
        var starter = new ServiceStarter(() => "/x/Trofeo.Display.exe", _ => throw new System.ComponentModel.Win32Exception("refusé"));

        Assert.False(starter.TryStart());
    }
}
