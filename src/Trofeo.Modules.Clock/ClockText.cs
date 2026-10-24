using System.Globalization;

namespace Trofeo.Modules.Clock;

/// <summary>Mise en forme des textes de l'heure et de la date.</summary>
public static class ClockText
{
    private static readonly CultureInfo French = new("fr-FR");

    /// <summary>Heure locale : <c>"14:32"</c> (24 h) ou <c>"2:32 PM"</c> (12 h).</summary>
    public static string Time(DateTime local, ClockFormat f) => f switch
    {
        ClockFormat.H12 => local.ToString("h:mm tt", CultureInfo.InvariantCulture),
        _ => local.ToString("HH:mm", CultureInfo.InvariantCulture),
    };

    /// <summary>Date locale en français, première lettre en majuscule : <c>"Samedi 24 octobre"</c>.</summary>
    public static string Date(DateTime local)
    {
        var text = local.ToString("dddd d MMMM", French);
        return char.ToUpper(text[0], French) + text[1..];
    }
}
