using System.Text.Json;
using Trofeo.Modules.Clock;

namespace Trofeo.Modules.Clock.Tests;

public class ClockSettingsTests
{
    private static ClockSettings Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ClockSettings.FromJson(doc.RootElement);
    }

    [Theory]
    [InlineData("c", TemperatureUnit.Celsius)]
    [InlineData("f", TemperatureUnit.Fahrenheit)]
    [InlineData("k", TemperatureUnit.Celsius)]
    [InlineData("", TemperatureUnit.Celsius)]
    public void Settings_ParsesLocationAndUnit(string unit, TemperatureUnit expected)
    {
        var settings = Parse($$"""
            {"format":"12h","location":{"name":"Lyon","latitude":45.76,"longitude":4.84},"unit":"{{unit}}"}
            """);

        Assert.Equal(ClockFormat.H12, settings.Format);
        Assert.Equal(new WeatherLocation("Lyon", 45.76, 4.84), settings.Location);
        Assert.Equal(expected, settings.Unit);
    }

    [Fact]
    public void Settings_Defaults_WithoutLocationOrUnit()
    {
        var settings = Parse("{}");

        Assert.Equal(ClockFormat.H24, settings.Format);
        Assert.Null(settings.Location);
        Assert.Equal(TemperatureUnit.Celsius, settings.Unit);
    }

    [Theory]
    [InlineData("""{"location":{"name":"X","latitude":90.5,"longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"X","latitude":-91,"longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"X","latitude":45.76,"longitude":180.1}}""")]
    [InlineData("""{"location":{"name":"X","latitude":45.76,"longitude":-181}}""")]
    [InlineData("""{"location":{"name":"","latitude":45.76,"longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"   ","latitude":45.76,"longitude":4.84}}""")]
    [InlineData("""{"location":{"latitude":45.76,"longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"X","longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"X","latitude":"45.76","longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"X","latitude":null,"longitude":4.84}}""")]
    [InlineData("""{"location":{"name":"X","latitude":1e999,"longitude":4.84}}""")]
    [InlineData("""{"location":"Lyon"}""")]
    [InlineData("""{"location":null}""")]
    public void Settings_InvalidCoordinates_NoLocation(string json)
    {
        Assert.Null(Parse(json).Location);
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 0)]
    public void Settings_BoundaryCoordinates_AreAccepted(double latitude, double longitude)
    {
        var settings = Parse($$$"""{"location":{"name":"X","latitude":{{{latitude}}},"longitude":{{{longitude}}}}}""");

        Assert.Equal(new WeatherLocation("X", latitude, longitude), settings.Location);
    }

    [Fact]
    public void Settings_InvalidLocation_KeepsFormatAndUnit()
    {
        var settings = Parse("""{"format":"12h","unit":"f","location":{"name":"","latitude":1,"longitude":1}}""");

        Assert.Equal(ClockFormat.H12, settings.Format);
        Assert.Equal(TemperatureUnit.Fahrenheit, settings.Unit);
        Assert.Null(settings.Location);
    }
}
