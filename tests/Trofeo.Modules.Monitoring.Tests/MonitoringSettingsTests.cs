using System.Text.Json;
using Trofeo.ModuleKit;

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

    [Fact]
    public void MonitoringSettings_DuplicateRows_KeepFirst()
    {
        Assert.Equal(new[] { "cpu", "ram" }, Parse("""{"rows":["cpu","ram","cpu"]}""").Rows);
        Assert.Equal(new[] { "ram", "cpu" }, Parse("""{"rows":["ram"," CPU ","Ram","cpu"]}""").Rows);
    }

    [Fact]
    public void Monitoring_Schema_HasTitleRowsAndGpu()
    {
        var fields = ((IConfigurableModuleFactory)new MonitoringFactory(null!)).Schema.Fields;

        Assert.Equal(["title", "rows", "gpu"], fields.Select(f => f.Key));

        var title = Assert.IsType<TextField>(fields[0]);
        Assert.Equal("Titre", title.Label);
        Assert.Equal("Nom du matériel détecté", title.Placeholder);
        Assert.Equal(60, title.MaxLength);

        var rows = Assert.IsType<ListField>(fields[1]);
        Assert.Equal("Lignes affichées", rows.Label);
        Assert.Equal(
            [
                new SettingOption("cpu", "Processeur"), new SettingOption("gpu", "Carte graphique"),
                new SettingOption("ram", "Mémoire"), new SettingOption("vram", "Mémoire vidéo"),
                new SettingOption("clock", "Fréquences"), new SettingOption("power", "Consommation"),
                new SettingOption("fan", "Ventilateur"), new SettingOption("disk", "Disque"),
            ],
            rows.Options);
        Assert.Equal(["cpu", "gpu", "ram"], rows.Default);
        Assert.All(rows.Options, o => Assert.True(MonitoringRows.IsKnown(o.Value)));
    }
}
