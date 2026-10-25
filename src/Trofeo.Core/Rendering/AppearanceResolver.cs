namespace Trofeo.Core.Rendering;

/// <summary>Transforme les réglages d'apparence en thème complet, sans jamais lever d'exception pour un réglage faux.</summary>
public static class AppearanceResolver
{
    /// <summary>
    /// Thème inconnu : Classique, avec un avertissement. Opacité bornée à [0, 1] ; NaN : celle du thème, avec un avertissement.
    /// Police : <see cref="FontResolver"/> ; si elle est trouvée, <c>Typeface</c> et <c>FeedTypeface</c> la prennent et
    /// <c>FeedScale</c> revient à 1. Si elle est introuvable, le thème reste tel quel.
    /// </summary>
    public static ThemeDefinition Resolve(AppearanceSettings settings, ISystemFonts fonts, Action<string>? warn = null)
    {
        var definition = Themes.Find(settings.Theme);
        if (definition is null)
        {
            warn?.Invoke($"Thème « {settings.Theme} » inconnu : thème Classique utilisé.");
            definition = Themes.ClassiqueDefinition;
        }

        if (settings.PanelOpacity is { } opacity)
        {
            if (float.IsNaN(opacity))
                warn?.Invoke("Opacité des modules invalide : thème par défaut utilisé.");
            else
                definition = definition with { Theme = definition.Theme with { PanelOpacity = Math.Clamp(opacity, 0f, 1f) } };
        }

        if (!string.IsNullOrWhiteSpace(settings.Font))
        {
            var face = FontResolver.Resolve(settings.Font, definition.Theme.Typeface, fonts, warn);
            if (!ReferenceEquals(face, definition.Theme.Typeface))
                definition = definition with { Theme = definition.Theme with { Typeface = face, FeedTypeface = face, FeedScale = 1f } };
        }

        return definition;
    }
}
