using System.Globalization;
using System.Text.Json;

namespace Trofeo.Modules.Clock;

/// <summary>Client d'Open-Meteo (prévisions et recherche de ville), sans clé d'API.</summary>
public sealed class OpenMeteoClient(HttpClient http) : IWeatherProvider
{
    /// <summary>Durée maximale d'une requête, réponse comprise (modifiable pour les tests).</summary>
    internal TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<WeatherNow> GetCurrentAsync(WeatherLocation where, TemperatureUnit unit, CancellationToken ct)
    {
        var url = "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={Number(where.Latitude)}&longitude={Number(where.Longitude)}"
            + "&current=temperature_2m,weather_code,is_day"
            + $"&temperature_unit={(unit == TemperatureUnit.Fahrenheit ? "fahrenheit" : "celsius")}&timezone=auto";

        using var doc = await GetJsonAsync(url, ct);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("current", out var current)
            && current.ValueKind == JsonValueKind.Object
            && TryDouble(current, "temperature_2m", out var temperature)
            && current.TryGetProperty("weather_code", out var code) && code.TryGetInt32(out var weatherCode)
            && current.TryGetProperty("is_day", out var isDay) && isDay.TryGetInt32(out var day))
        {
            return new WeatherNow(temperature, weatherCode, day == 1);
        }

        throw new WeatherException("Réponse météo incomplète.");
    }

    /// <summary>Jusqu'à 5 villes correspondant à <paramref name="name"/>, noms en français ; vide si aucune.</summary>
    /// <exception cref="WeatherException">Réponse indisponible, illisible ou trop lente.</exception>
    public async Task<IReadOnlyList<CitySuggestion>> SearchCityAsync(string name, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=5&language=fr&format=json";

        using var doc = await GetJsonAsync(url, ct);
        var cities = new List<CitySuggestion>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return cities;
        }

        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && Text(item, "name") is { Length: > 0 } cityName
                && TryDouble(item, "latitude", out var latitude)
                && TryDouble(item, "longitude", out var longitude))
            {
                cities.Add(new CitySuggestion(cityName, Text(item, "admin1"), Text(item, "country"), latitude, longitude));
            }
        }

        return cities;
    }

    /// <summary>Télécharge et analyse la réponse ; toute panne devient une <see cref="WeatherException"/>.</summary>
    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        // Le délai est porté par un jeton lié plutôt que par HttpClient.Timeout, qui est celui de l'hôte.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await http.GetAsync(url, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherException($"Open-Meteo a répondu {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new WeatherException("Open-Meteo n'a pas répondu à temps.");
        }
        catch (HttpRequestException ex)
        {
            throw new WeatherException($"Open-Meteo est injoignable : {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new WeatherException("Réponse d'Open-Meteo illisible.", ex);
        }
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryDouble(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
