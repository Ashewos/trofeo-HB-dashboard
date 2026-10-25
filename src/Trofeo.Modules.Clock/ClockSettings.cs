using System.Text.Json;

namespace Trofeo.Modules.Clock;

/// <summary>Format d'affichage de l'heure.</summary>
public enum ClockFormat { H24, H12 }

/// <summary>
/// Réglages du module Heure, date et météo, lus de
/// <c>{"format":"24h"|"12h","location":{"name":"Lyon","latitude":45.76,"longitude":4.84},"unit":"c"|"f"}</c>.
/// </summary>
public sealed record ClockSettings(
    ClockFormat Format = ClockFormat.H24,
    WeatherLocation? Location = null,
    TemperatureUnit Unit = TemperatureUnit.Celsius)
{
    /// <summary>
    /// Lit les réglages sans jamais lever : un format absent ou inconnu donne le 24 h, une unité absente ou
    /// inconnue le Celsius, et un lieu absent ou invalide (nom vide, coordonnées hors limites) aucune météo.
    /// </summary>
    public static ClockSettings FromJson(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return new ClockSettings();

        var format = settings.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() == "12h"
            ? ClockFormat.H12
            : ClockFormat.H24;
        var unit = settings.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String && u.GetString() == "f"
            ? TemperatureUnit.Fahrenheit
            : TemperatureUnit.Celsius;
        var location = settings.TryGetProperty("location", out var l) ? ParseLocation(l) : null;

        return new ClockSettings(format, location, unit);
    }

    private static WeatherLocation? ParseLocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
            || !TryCoordinate(element, "latitude", 90, out var latitude)
            || !TryCoordinate(element, "longitude", 180, out var longitude))
        {
            return null;
        }

        var text = name.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : new WeatherLocation(text, latitude, longitude);
    }

    /// <summary>Lit un nombre fini compris entre −<paramref name="limit"/> et <paramref name="limit"/>.</summary>
    private static bool TryCoordinate(JsonElement parent, string name, double limit, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value)
            && Math.Abs(value) <= limit;
    }
}
