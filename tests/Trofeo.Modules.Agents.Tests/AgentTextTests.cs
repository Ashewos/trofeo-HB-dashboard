namespace Trofeo.Modules.Agents.Tests;

public class AgentTextTests
{
    // Le 26 octobre 2026 est un lundi.
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);
    private static DateTimeOffset At(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, Offset);

    [Theory]
    [InlineData(20, "< 1 min")]
    [InlineData(12 * 60 + 30, "12 min")]
    [InlineData(65 * 60, "1 h 05")]
    public void Duration_Formats(int seconds, string expected) =>
        Assert.Equal(expected, AgentText.Duration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(41.5, "42 %")]
    [InlineData(130, "100 %")]
    [InlineData(-3, "0 %")]
    public void Percent_RoundsAndClamps(double value, string expected) =>
        Assert.Equal(expected, AgentText.Percent(value));

    [Fact]
    public void Reset_SameDay_ShowsTime() =>
        Assert.Equal("reset 14:30", AgentText.Reset(At(10, 25, 14, 30), At(10, 25, 9, 0)));

    [Fact]
    public void Reset_ThisWeek_ShowsDay() =>
        Assert.Equal("reset lun. 09:00", AgentText.Reset(At(10, 26, 9, 0), At(10, 25, 9, 0)));

    [Fact]
    public void Reset_BeyondAWeek_ShowsDate() =>
        Assert.Equal("reset 3 nov.", AgentText.Reset(At(11, 3, 9, 0), At(10, 25, 9, 0)));

    [Fact]
    public void Reset_Past() =>
        Assert.Equal("reset passé", AgentText.Reset(At(10, 25, 8, 0), At(10, 25, 9, 0)));

    [Fact]
    public void Reset_UsesGivenLocalTimeNotMachineZone()
    {
        // Les deux dates sont déjà locales à l'appelant (ici +09:00) : l'heure affichée est celle reçue.
        var offset = TimeSpan.FromHours(9);
        var now = new DateTimeOffset(2026, 10, 25, 23, 0, 0, offset);
        var resets = new DateTimeOffset(2026, 10, 26, 1, 15, 0, offset);
        Assert.Equal("reset lun. 01:15", AgentText.Reset(resets, now));
    }
}
