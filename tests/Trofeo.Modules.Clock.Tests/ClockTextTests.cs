using System.Text.Json;
using Trofeo.Modules.Clock;

namespace Trofeo.Modules.Clock.Tests;

public class ClockTextTests
{
    [Theory]
    [InlineData(14, 32, "14:32")]
    [InlineData(9, 5, "09:05")]
    public void Time_24h(int hour, int minute, string expected)
    {
        var local = new DateTime(2026, 10, 24, hour, minute, 0);
        Assert.Equal(expected, ClockText.Time(local, ClockFormat.H24));
    }

    [Theory]
    [InlineData(14, 32, "2:32 PM")]
    [InlineData(0, 5, "12:05 AM")]
    public void Time_12h(int hour, int minute, string expected)
    {
        var local = new DateTime(2026, 10, 24, hour, minute, 0);
        Assert.Equal(expected, ClockText.Time(local, ClockFormat.H12));
    }

    [Fact]
    public void Date_French()
    {
        Assert.Equal("Samedi 24 octobre", ClockText.Date(new DateTime(2026, 10, 24, 14, 32, 0)));
    }

    [Fact]
    public void Settings_Parse12h()
    {
        using var doc = JsonDocument.Parse("""{"format":"12h"}""");
        Assert.Equal(ClockFormat.H12, ClockSettings.FromJson(doc.RootElement).Format);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"format":"24h"}""")]
    [InlineData("""{"format":"bizarre"}""")]
    [InlineData("""{"format":12}""")]
    [InlineData("null")]
    public void Settings_MissingOrUnknown_DefaultsTo24h(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ClockFormat.H24, ClockSettings.FromJson(doc.RootElement).Format);
    }

    [Fact]
    public void Settings_UndefinedElement_DefaultsTo24h()
    {
        Assert.Equal(ClockFormat.H24, ClockSettings.FromJson(default).Format);
    }
}
