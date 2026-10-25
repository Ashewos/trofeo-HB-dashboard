using System.Globalization;

namespace Trofeo.Modules.Network;

/// <summary>Mise en forme d'un débit pour l'affichage : valeur et unité, en bits par seconde.</summary>
public static class RateText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Convertit des octets par seconde en bits par seconde puis choisit l'unité : kb/s entier sous 1 Mb/s,
    /// Mb/s (une décimale sous 10) sous 1 Gb/s, Gb/s avec une décimale au-delà. Une valeur négative ou NaN
    /// compte pour 0. La valeur arrondie décide de l'unité (999,7 kb/s s'écrit « 1,0 Mb/s », pas « 1000 kb/s »).
    /// </summary>
    public static (string Value, string Unit) Format(double bytesPerSecond)
    {
        var bits = double.IsNaN(bytesPerSecond) || bytesPerSecond < 0 ? 0 : bytesPerSecond * 8;

        var kilo = Math.Round(bits / 1e3, MidpointRounding.AwayFromZero);
        if (kilo < 1000) return (kilo.ToString("0", French), "kb/s");

        var mega = Math.Round(bits / 1e6, bits / 1e6 < 10 ? 1 : 0, MidpointRounding.AwayFromZero);
        if (mega < 1000) return (mega.ToString(mega < 10 ? "0.0" : "0", French), "Mb/s");

        return (Math.Round(bits / 1e9, 1, MidpointRounding.AwayFromZero).ToString("0.0", French), "Gb/s");
    }
}
