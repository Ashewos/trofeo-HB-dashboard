using System.Text.Json;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Clock.Tests;

public class ClockFactoryTests
{
    private static IConfigurableModuleFactory Factory(FakeWeatherProvider weather) => new ClockFactory(weather);

    [Fact]
    public void Clock_Schema_HasFormatUnitLocation()
    {
        var fields = Factory(new FakeWeatherProvider()).Schema.Fields;

        Assert.Equal(["format", "unit", "location"], fields.Select(f => f.Key));

        var format = Assert.IsType<ChoiceField>(fields[0]);
        Assert.Equal("Format de l'heure", format.Label);
        Assert.Equal([new SettingOption("24h", "24 h"), new SettingOption("12h", "12 h (AM/PM)")], format.Options);
        Assert.Equal("24h", format.Default);

        var unit = Assert.IsType<ChoiceField>(fields[1]);
        Assert.Equal("Unité", unit.Label);
        Assert.Equal([new SettingOption("c", "°C"), new SettingOption("f", "°F")], unit.Options);
        Assert.Equal("c", unit.Default);

        var location = Assert.IsType<LookupField>(fields[2]);
        Assert.Equal("Ville de la météo", location.Label);
        Assert.Equal("Cherche une ville", location.Placeholder);
        Assert.Equal("Pas de météo", location.EmptyLabel);
        Assert.True(location.Searchable);
    }

    [Fact]
    public async Task Clock_Lookup_FormatsLabelAndValue()
    {
        var weather = new FakeWeatherProvider
        {
            Search = _ => Task.FromResult<IReadOnlyList<CitySuggestion>>(
                [new CitySuggestion("Lyon", "Auvergne-Rhône-Alpes", "France", 45.76, 4.84)]),
        };

        var results = await Factory(weather).LookupAsync("location", "Lyon", CancellationToken.None);

        var one = Assert.Single(results);
        Assert.Equal("Lyon, Auvergne-Rhône-Alpes, France", one.Label);
        Assert.Equal("Lyon", one.Value.GetProperty("name").GetString());
        Assert.Equal(45.76, one.Value.GetProperty("latitude").GetDouble());
        Assert.Equal(4.84, one.Value.GetProperty("longitude").GetDouble());
        Assert.Equal("Lyon", weather.LastSearch);
    }

    [Fact]
    public async Task Clock_Lookup_SkipsMissingParts()
    {
        var weather = new FakeWeatherProvider
        {
            Search = _ => Task.FromResult<IReadOnlyList<CitySuggestion>>(
                [new CitySuggestion("Lyon", null, "France", 45.76, 4.84), new CitySuggestion("Nice", "  ", null, 43.7, 7.26)]),
        };

        var results = await Factory(weather).LookupAsync("location", "x", CancellationToken.None);

        Assert.Equal(["Lyon, France", "Nice"], results.Select(r => r.Label));
    }

    [Fact]
    public async Task Clock_Lookup_ReturnsAtMostFive_AndIgnoresOtherFields()
    {
        var many = Enumerable.Range(0, 8).Select(i => new CitySuggestion($"V{i}", null, null, i, i)).ToArray();
        var weather = new FakeWeatherProvider { Search = _ => Task.FromResult<IReadOnlyList<CitySuggestion>>(many) };
        var factory = Factory(weather);

        Assert.Equal(5, (await factory.LookupAsync("location", "v", CancellationToken.None)).Count);
        Assert.Empty(await factory.LookupAsync("format", "v", CancellationToken.None));
    }

    [Fact]
    public async Task Clock_Lookup_BlankQuery_DoesNotSearch()
    {
        var weather = new FakeWeatherProvider();

        var results = await Factory(weather).LookupAsync("location", "  ", CancellationToken.None);

        Assert.Empty(results);
        Assert.Null(weather.LastSearch);
    }

    [Fact]
    public void Clock_LookupValue_ReadsBackAsSettingsLocation()
    {
        var value = JsonDocument.Parse("""{"name":"Lyon","latitude":45.76,"longitude":4.84}""").RootElement;
        var settings = SettingsDocument.Set(default, "location", value);

        Assert.Equal(new WeatherLocation("Lyon", 45.76, 4.84), ClockSettings.FromJson(settings).Location);
    }
}
