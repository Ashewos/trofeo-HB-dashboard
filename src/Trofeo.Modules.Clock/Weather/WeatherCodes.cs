namespace Trofeo.Modules.Clock;

/// <summary>Icône météo, une par famille de conditions.</summary>
public enum WeatherIcon { Sun, Moon, PartlyCloudyDay, PartlyCloudyNight, Cloud, Fog, Drizzle, Rain, Snow, Storm, Unknown }

/// <summary>Traduction des codes météo WMO d'Open-Meteo en icône et libellé français.</summary>
public static class WeatherCodes
{
    /// <summary>Icône et libellé d'un code ; « — » et <see cref="WeatherIcon.Unknown"/> pour un code inconnu.</summary>
    public static (WeatherIcon Icon, string Label) Describe(int code, bool isDay) => code switch
    {
        0 => isDay ? (WeatherIcon.Sun, "Ensoleillé") : (WeatherIcon.Moon, "Dégagé"),
        1 => (PartlyCloudy(isDay), "Peu nuageux"),
        2 => (PartlyCloudy(isDay), "Éclaircies"),
        3 => (WeatherIcon.Cloud, "Couvert"),
        45 or 48 => (WeatherIcon.Fog, "Brouillard"),
        51 or 53 or 55 => (WeatherIcon.Drizzle, "Bruine"),
        56 or 57 => (WeatherIcon.Drizzle, "Bruine verglaçante"),
        61 or 63 => (WeatherIcon.Rain, "Pluie"),
        65 => (WeatherIcon.Rain, "Forte pluie"),
        66 or 67 => (WeatherIcon.Rain, "Pluie verglaçante"),
        71 or 73 => (WeatherIcon.Snow, "Neige"),
        75 => (WeatherIcon.Snow, "Forte neige"),
        77 => (WeatherIcon.Snow, "Grains de neige"),
        80 or 81 => (WeatherIcon.Rain, "Averses"),
        82 => (WeatherIcon.Rain, "Fortes averses"),
        85 or 86 => (WeatherIcon.Snow, "Averses de neige"),
        95 => (WeatherIcon.Storm, "Orage"),
        96 or 99 => (WeatherIcon.Storm, "Orage et grêle"),
        _ => (WeatherIcon.Unknown, "—"),
    };

    private static WeatherIcon PartlyCloudy(bool isDay) => isDay ? WeatherIcon.PartlyCloudyDay : WeatherIcon.PartlyCloudyNight;
}
