namespace Trofeo.Core.Rendering;

/// <summary>Transforme les réglages d'apparence en thème complet, sans jamais lever d'exception pour un réglage faux.</summary>
public static class AppearanceResolver
{
    /// <summary>
    /// Thème inconnu : Classique, avec un avertissement. Opacité bornée à [0, 1] ; NaN : celle du thème, avec un avertissement.
    /// Police : <see cref="FontResolver"/> ; si elle est trouvée, <c>Typeface</c> et <c>FeedTypeface</c> la prennent, et
    /// <c>TypefaceScale</c> comme <c>FeedScale</c> prennent l'agrandissement de cette police
    /// (<see cref="EmbeddedFonts.ScaleOf"/> : 1,24 pour VT323, même en Classique ; 1 pour toute autre police, du système comprise). Si elle est introuvable, le thème reste tel quel ; si c'est la police du thème elle-même, le fil la prend aussi.
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
            // FontResolver rend le repli et avertit quand la police est introuvable : l'avertissement distingue
            // « introuvable » de « trouvée, et c'est justement celle du thème » (instance embarquée partagée).
            var notFound = false;
            var face = FontResolver.Resolve(settings.Font, definition.Theme.Typeface, fonts, message =>
            {
                notFound = true;
                warn?.Invoke(message);
            });
            if (!notFound)
            {
                var scale = EmbeddedFonts.ScaleOf(settings.Font);
                definition = definition with
                {
                    Theme = definition.Theme with { Typeface = face, TypefaceScale = scale, FeedTypeface = face, FeedScale = scale },
                };
            }
        }

        return definition;
    }
}
