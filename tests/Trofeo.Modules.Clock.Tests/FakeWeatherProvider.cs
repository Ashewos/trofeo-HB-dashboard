using Trofeo.Modules.Clock;

namespace Trofeo.Modules.Clock.Tests;

/// <summary>Fournisseur de météo factice : enregistre les appels et renvoie ce que le test a programmé.</summary>
internal sealed class FakeWeatherProvider : IWeatherProvider
{
    public int Calls { get; private set; }
    public WeatherLocation? LastLocation { get; private set; }
    public TemperatureUnit? LastUnit { get; private set; }
    public CancellationToken LastToken { get; private set; }

    /// <summary>Réponse programmée ; lever une exception simule un échec.</summary>
    public Func<Task<WeatherNow>> Respond { get; set; } = () => Task.FromResult(new WeatherNow(12.4, 2, true));

    public Task<WeatherNow> GetCurrentAsync(WeatherLocation where, TemperatureUnit unit, CancellationToken ct)
    {
        Calls++;
        LastLocation = where;
        LastUnit = unit;
        LastToken = ct;
        return Respond();
    }

    public string? LastSearch { get; private set; }

    /// <summary>Villes programmées pour la recherche ; lever une exception simule un échec.</summary>
    public Func<string, Task<IReadOnlyList<CitySuggestion>>> Search { get; set; } = _ => Task.FromResult<IReadOnlyList<CitySuggestion>>([]);

    public Task<IReadOnlyList<CitySuggestion>> SearchCityAsync(string name, CancellationToken ct)
    {
        LastSearch = name;
        return Search(name);
    }
}
