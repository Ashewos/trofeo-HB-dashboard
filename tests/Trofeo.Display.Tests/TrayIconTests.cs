using Trofeo.Device;

namespace Trofeo.Display.Tests;

public sealed class TrayIconTests
{
    [Theory]
    [InlineData(ScreenState.Ready, null, "Trofeo dashboard — écran : prêt")]
    [InlineData(ScreenState.Ready, "ignoré", "Trofeo dashboard — écran : prêt")]
    [InlineData(ScreenState.Recovering, null, "Trofeo dashboard — écran : reprise…")]
    [InlineData(ScreenState.Disconnected, null, "Trofeo dashboard — écran : non branché")]
    [InlineData(ScreenState.Disconnected, "Écran non branché", "Trofeo dashboard — écran : Écran non branché")]
    [InlineData(ScreenState.Disconnected, ScreenDevice.BusyReason, "Trofeo dashboard — écran : " + ScreenDevice.BusyReason)]
    public void Tooltip_MapsScreenStateAndReason(ScreenState state, string? reason, string expected)
    {
        Assert.Equal(expected, TrayIcon.Tooltip(state, reason));
    }

    [Fact]
    public void Tooltip_LongReason_IsTruncatedTo127Chars()
    {
        var tooltip = TrayIcon.Tooltip(ScreenState.Disconnected, new string('x', 500));

        Assert.Equal(127, tooltip.Length);
        Assert.StartsWith("Trofeo dashboard — écran : xxx", tooltip);
    }
}
