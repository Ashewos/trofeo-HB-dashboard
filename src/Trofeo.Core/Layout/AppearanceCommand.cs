using System.Globalization;
using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Layout;

/// <summary>
/// La commande <c>HardwareCheck --appearance</c> : affiche ou change le thème, l'opacité, la police et le fond dans
/// <c>layout.json</c>, avec des messages en français, en gardant tout le reste du fichier (modules, raccourcis, rotation).
/// Les flux sont passés en paramètres pour que les tests lisent ce qui est affiché.
/// </summary>
public static class AppearanceCommand
{
    /// <summary>Le texte d'aide, tel qu'il est affiché sur une erreur d'arguments.</summary>
    public const string Usage =
        "Usage : Trofeo.HardwareCheck --appearance [theme=<id>] [opacity=<0..1|theme>] [font=<famille|theme>]"
        + " [background=theme|color:#hex|image:<chemin>|gif:<chemin>|video:<chemin>] [legibility=on|off|theme]";

    private const string FromTheme = "theme";

    /// <summary>
    /// Rend le code de sortie : 0 réussi, 1 échec (fichier illisible ou impossible à écrire), 2 mauvais arguments ou valeur refusée
    /// (le fichier n'est alors pas touché). Sans réglage, affiche les réglages actuels puis les polices du système.
    /// </summary>
    /// <param name="layoutPath">Le fichier <c>layout.json</c>.</param>
    /// <param name="args">Ce qui suit <c>--appearance</c> : des réglages <c>clé=valeur</c>.</param>
    public static int Run(string layoutPath, string[] args, ISystemFonts fonts, TextWriter output, TextWriter error)
    {
        var store = new LayoutStore(layoutPath, output.WriteLine);
        DashboardLayout layout;
        try
        {
            layout = store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(error, $"Impossible de lire {layoutPath} : {ex.Message}", 1);
        }

        // Load rend l'instance Default quand le fichier existe mais est illisible : on ne l'écrase pas par la disposition par défaut.
        if (ReferenceEquals(layout, DashboardLayout.Default) && File.Exists(layoutPath))
        {
            return Fail(error, $"{layoutPath} est illisible ou invalide : rien n'a été changé. Une copie est gardée en .bad.", 1);
        }

        var current = layout.Appearance ?? AppearanceSettings.Default;
        if (args.Length == 0)
        {
            Describe(current, output);
            output.WriteLine();
            output.WriteLine("Polices du système (pour font=<famille>) :");
            foreach (var family in fonts.Families) output.WriteLine($"  {family}");
            return 0;
        }

        var updated = current;
        foreach (var arg in args)
        {
            var separator = arg.IndexOf('=');
            if (separator <= 0) return Fail(error, $"Réglage « {arg} » incompris (attendu : clé=valeur).{CommaHint(arg)}\n{Usage}", 2);

            var key = arg[..separator].Trim().ToLowerInvariant();
            var value = arg[(separator + 1)..].Trim();
            var (next, problem) = key switch
            {
                "theme" => ApplyTheme(updated, value),
                "opacity" => ApplyOpacity(updated, value),
                "font" => ApplyFont(updated, value, fonts, output),
                "background" => ApplyBackground(updated, value),
                "legibility" => ApplyLegibility(updated, value),
                _ => (updated, $"Réglage « {key} » inconnu (theme, opacity, font, background ou legibility).\n{Usage}"),
            };
            if (problem is not null) return Fail(error, problem, 2);
            updated = next;
        }

        try
        {
            store.Save(layout with { Appearance = updated });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pas de trace de pile : le message de Windows dit déjà ce qui bloque (fichier ouvert, lecture seule…).
            return Fail(error, $"Impossible d'écrire {layoutPath} : {ex.Message}", 1);
        }

        output.WriteLine($"Apparence enregistrée dans {layoutPath}.");
        Describe(updated, output);
        output.WriteLine("Relance le service d'affichage pour appliquer ces réglages.");
        return 0;
    }

    /// <summary>
    /// Un nombre seul (« 5 ») est la fin d'une valeur que le terminal a coupée à la virgule (<c>opacity=0,5</c> devient
    /// <c>opacity=0</c> puis <c>5</c>, par exemple avec <c>Start-Process -ArgumentList</c> de PowerShell) : on dit d'écrire un point.
    /// </summary>
    private static string CommaHint(string arg) =>
        double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? " Si c'est la fin d'un nombre coupé à la virgule (opacity=0,5), écris-le avec un point : opacity=0.5."
            : "";

    private static (AppearanceSettings, string?) ApplyTheme(AppearanceSettings settings, string id)
    {
        var theme = Themes.Find(id);
        if (theme is null)
        {
            var known = string.Join(", ", Themes.All.Select(t => t.Theme.Id));
            return (settings, $"Thème « {id} » inconnu. Thèmes disponibles : {known}.");
        }

        return (settings with { Theme = theme.Theme.Id }, null);
    }

    private static (AppearanceSettings, string?) ApplyOpacity(AppearanceSettings settings, string value)
    {
        if (string.Equals(value, FromTheme, StringComparison.OrdinalIgnoreCase)) return (settings with { PanelOpacity = null }, null);

        // La virgule française est acceptée : « 0,5 ».
        if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
            && float.IsFinite(opacity) && opacity is >= 0f and <= 1f)
        {
            return (settings with { PanelOpacity = opacity }, null);
        }

        return (settings, $"Opacité « {value} » invalide : un nombre de 0 à 1 (par exemple 0,5), ou « theme » pour celle du thème.");
    }

    private static (AppearanceSettings, string?) ApplyFont(
        AppearanceSettings settings, string value, ISystemFonts fonts, TextWriter output)
    {
        if (value.Length == 0) return (settings, "Police vide : une famille, ou « theme » pour celle du thème.");
        if (string.Equals(value, FromTheme, StringComparison.OrdinalIgnoreCase)) return (settings with { Font = null }, null);

        // Une police absente est acceptée (elle peut s'installer plus tard) : le service retombe sur celle du thème.
        if (EmbeddedFonts.Find(value) is null && fonts.Match(value) is null)
        {
            output.WriteLine(
                $"Attention : la police « {value} » n'est pas installée. Le service utilisera la police du thème tant qu'elle manque.");
        }

        return (settings with { Font = value }, null);
    }

    private static (AppearanceSettings, string?) ApplyLegibility(AppearanceSettings settings, string value)
    {
        // « on » est le défaut : null et true veulent dire la même chose, mais « on » reste écrit tel quel pour qu'on le voie dans le fichier.
        if (string.Equals(value, FromTheme, StringComparison.OrdinalIgnoreCase)) return (settings with { Legibility = null }, null);
        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)) return (settings with { Legibility = true }, null);
        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return (settings with { Legibility = false }, null);

        return (settings, $"Lisibilité « {value} » invalide : on (garde active), off (garde coupée) ou « theme » pour le défaut (legibility=on|off|theme).");
    }

    private static (AppearanceSettings, string?) ApplyBackground(AppearanceSettings settings, string value)
    {
        if (string.Equals(value, FromTheme, StringComparison.OrdinalIgnoreCase)) return (settings with { Background = null }, null);

        var colon = value.IndexOf(':');
        var kind = colon > 0 ? value[..colon].Trim().ToLowerInvariant() : value.ToLowerInvariant();
        var argument = colon > 0 ? value[(colon + 1)..].Trim() : "";
        switch (kind)
        {
            case "color":
                if (!SKColor.TryParse(argument, out _))
                    return (settings, $"Couleur « {argument} » invalide : écris-la comme #RRGGBB (par exemple color:#1A2B3C).");
                return (settings with { Background = new BackgroundSettings(BackgroundKind.Color, Color: argument) }, null);

            case "image" or "gif" or "video":
                if (argument.Length == 0) return (settings, $"Chemin manquant : {kind}:<chemin du fichier>.");
                var path = Path.GetFullPath(argument); // le service ne tourne pas dans ce dossier : on enregistre un chemin complet
                if (!File.Exists(path)) return (settings, $"Fichier introuvable : {path}");
                var backgroundKind = kind switch { "image" => BackgroundKind.Image, "gif" => BackgroundKind.Animated, _ => BackgroundKind.Video };
                return (settings with { Background = new BackgroundSettings(backgroundKind, Path: path) }, null);

            default:
                return (settings, $"Fond « {value} » inconnu : theme, color:#hex, image:<chemin>, gif:<chemin> ou video:<chemin>.");
        }
    }

    private static void Describe(AppearanceSettings settings, TextWriter output)
    {
        output.WriteLine($"Thème      : {settings.Theme}");
        output.WriteLine($"Opacité    : {(settings.PanelOpacity is { } opacity ? opacity.ToString("0.##", CultureInfo.InvariantCulture) : "celle du thème")}");
        output.WriteLine($"Police     : {(string.IsNullOrWhiteSpace(settings.Font) ? "celle du thème" : settings.Font)}");
        output.WriteLine($"Fond       : {DescribeBackground(settings.Background)}");
        output.WriteLine($"Lisibilité : {(settings.Legibility == false ? "garde coupée" : "garde active")}");
    }

    private static string DescribeBackground(BackgroundSettings? background) => background?.Kind switch
    {
        BackgroundKind.Color => $"couleur {background.Color}",
        BackgroundKind.Image => $"image {background.Path}",
        BackgroundKind.Animated => $"GIF {background.Path}",
        BackgroundKind.Video => $"vidéo {background.Path}",
        _ => "celui du thème",
    };

    private static int Fail(TextWriter error, string message, int code)
    {
        error.WriteLine(message);
        return code;
    }
}
