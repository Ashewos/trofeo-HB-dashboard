namespace Trofeo.Modules.Clock;

/// <summary>Unité d'affichage de la température.</summary>
public enum TemperatureUnit { Celsius, Fahrenheit }

/// <summary>Lieu dont on affiche la météo : nom d'usage et coordonnées.</summary>
public sealed record WeatherLocation(string Name, double Latitude, double Longitude);

/// <summary>Conditions actuelles : température (dans l'unité demandée), code météo WMO et indicateur jour/nuit.</summary>
public sealed record WeatherNow(double Temperature, int WeatherCode, bool IsDay);

/// <summary>Ville proposée par la recherche : nom, région et pays pour la distinguer de ses homonymes.</summary>
public sealed record CitySuggestion(string Name, string? Region, string? Country, double Latitude, double Longitude);

/// <summary>Source de la météo actuelle.</summary>
public interface IWeatherProvider
{
    /// <exception cref="WeatherException">Réponse indisponible, illisible ou trop lente.</exception>
    Task<WeatherNow> GetCurrentAsync(WeatherLocation where, TemperatureUnit unit, CancellationToken ct);

    /// <summary>Jusqu'à 5 villes correspondant à <paramref name="name"/> ; vide si aucune.</summary>
    /// <exception cref="WeatherException">Réponse indisponible, illisible ou trop lente.</exception>
    Task<IReadOnlyList<CitySuggestion>> SearchCityAsync(string name, CancellationToken ct);
}

/// <summary>Échec d'une requête météo (réseau, statut HTTP, réponse illisible ou délai dépassé).</summary>
public sealed class WeatherException(string message, Exception? inner = null) : Exception(message, inner);
