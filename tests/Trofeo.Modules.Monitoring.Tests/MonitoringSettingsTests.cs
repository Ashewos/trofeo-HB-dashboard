using System.Text.Json;

namespace Trofeo.Modules.Monitoring.Tests;

public class MonitoringSettingsTests
{
    private static MonitoringSettings Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MonitoringSettings.FromJson(doc.RootElement);
    }

    [Fact]
    public void Settings_Defaults()
    {
        var s = Parse("{}");

        Assert.Null(s.Title);
        Assert.Equal(new[] { "cpu", "gpu", "ram" }, s.Rows);
    }

    [Fact]
    public void Settings_ReadsTitleAndRows()
    {
        var s = Parse("""{"title":"Mon PC","rows":["ram","cpu","disk"]}""");

        Assert.Equal("Mon PC", s.Title);
        Assert.Equal(new[] { "ram", "cpu", "disk" }, s.Rows);
    }

    [Fact]
    public void Settings_UnknownRowsIgnored()
    {
        var s = Parse("""{"rows":["cpu","bogus","RAM","vram"]}""");

        Assert.Equal(new[] { "cpu", "ram", "vram" }, s.Rows);
    }

    [Fact]
    public void Settings_OnlyUnknownRows_FallBackToDefaults() =>
        Assert.Equal(new[] { "cpu", "gpu", "ram" }, Parse("""{"rows":["bogus"]}""").Rows);

    [Theory]
    [InlineData("""{"title":""}""")]
    [InlineData("""{"title":"   "}""")]
    [InlineData("""{"title":null}""")]
    public void Settings_BlankTitle_IsNull(string json) => Assert.Null(Parse(json).Title);

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"texte\"")]
    [InlineData("""{"title":12,"rows":"cpu"}""")]
    [InlineData("""{"title":["a"],"rows":{"cpu":1}}""")]
    [InlineData("""{"rows":[1,null,{"a":1},["cpu"],true]}""")]
    public void Settings_MalformedShapes_FallBackToDefaultsWithoutThrowing(string json)
    {
        var s = Parse(json);

        Assert.Null(s.Title);
        Assert.Equal(new[] { "cpu", "gpu", "ram" }, s.Rows);
    }

    [Fact]
    public void Settings_DefaultElement_FallsBackToDefaults() =>
        Assert.Equal(new[] { "cpu", "gpu", "ram" }, MonitoringSettings.FromJson(default).Rows);
}
