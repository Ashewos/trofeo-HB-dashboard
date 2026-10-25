using System.Globalization;

namespace Trofeo.Modules.Agents;

/// <summary>Textes de la barre des modules Claude Code / Codex. Toujours en français, quelle que soit la culture du PC.</summary>
public static class AgentText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« &lt; 1 min », « 12 min », « 1 h 05 ».</summary>
    public static string Duration(TimeSpan elapsed)
    {
        var minutes = (int)Math.Max(0, elapsed.TotalMinutes);
        if (minutes < 1)
        {
            return "< 1 min";
        }

        return minutes < 60
            ? $"{minutes} min"
            : string.Create(French, $"{minutes / 60} h {minutes % 60:00}");
    }

    /// <summary>Pourcentage entier, arrondi au plus loin de zéro, borné à [0, 100] : « 42 % ».</summary>
    public static string Percent(double value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        return string.Create(French, $"{Math.Round(clamped, MidpointRounding.AwayFromZero)} %");
    }

    /// <summary>
    /// Moment de remise à zéro d'un quota. Les deux dates sont déjà à l'heure locale de l'appelant : leurs composantes
    /// sont comparées et affichées telles quelles, sans conversion de fuseau.
    /// </summary>
    public static string Reset(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        if (resetsAt.DateTime <= now.DateTime)
        {
            return "reset passé";
        }

        var days = (resetsAt.Date - now.Date).Days;
        if (days == 0)
        {
            return "reset " + resetsAt.ToString("HH:mm", French);
        }

        return days < 7
            ? "reset " + resetsAt.ToString("ddd HH:mm", French)
            : "reset " + resetsAt.ToString("d MMM", French);
    }
}
