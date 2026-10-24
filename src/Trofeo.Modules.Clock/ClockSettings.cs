using System.Text.Json;

namespace Trofeo.Modules.Clock;

/// <summary>Format d'affichage de l'heure.</summary>
public enum ClockFormat { H24, H12 }

/// <summary>Réglages du module Heure et date, lus de <c>{"format":"24h"|"12h"}</c>.</summary>
public sealed record ClockSettings(ClockFormat Format = ClockFormat.H24)
{
    /// <summary>Lit les réglages ; toute valeur absente ou inconnue donne le format 24 h.</summary>
    public static ClockSettings FromJson(JsonElement settings)
    {
        if (settings.ValueKind == JsonValueKind.Object
            && settings.TryGetProperty("format", out var format)
            && format.ValueKind == JsonValueKind.String
            && format.GetString() == "12h")
        {
            return new ClockSettings(ClockFormat.H12);
        }

        return new ClockSettings();
    }
}
