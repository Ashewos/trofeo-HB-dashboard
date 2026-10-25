using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>Choisit le fond de l'écran d'après les réglages ; un fond qui manque ne fait jamais planter le service.</summary>
public static class BackgroundFactory
{
    /// <summary>
    /// <paramref name="settings"/> null ou Kind Theme : fond par défaut du thème. Video : <paramref name="video"/>(chemin), ou
    /// le fond du thème si <paramref name="video"/> est null (avertissement « non prise en charge ») ou rend null (l'hôte a déjà averti). Fichier absent ou illisible, couleur mal écrite :
    /// fond du thème, et <paramref name="warn"/>.
    /// </summary>
    public static IBackground Create(BackgroundSettings? settings, ThemeDefinition theme,
        Func<string, IBackground?>? video = null, Action<string>? warn = null)
    {
        switch (settings?.Kind)
        {
            case BackgroundKind.Color:
                if (settings.Color is { Length: > 0 } text && SKColor.TryParse(text, out var color))
                {
                    return new ColorBackground(color);
                }
                warn?.Invoke(BackgroundSupport.BadColor(settings.Color));
                break;

            case BackgroundKind.Image:
                {
                    var image = ImageBackground.TryLoad(settings.Path ?? "", warn); // TryLoad avertit lui-même
                    if (image is not null) return image;
                    break;
                }

            case BackgroundKind.Animated:
                {
                    var animated = AnimatedBackground.TryLoad(settings.Path ?? "", warn);
                    if (animated is not null) return animated;
                    break;
                }

            case BackgroundKind.Video:
                {
                    var background = settings.Path is { Length: > 0 } path ? video?.Invoke(path) : null;
                    if (background is not null) return background;
                    // L'hôte qui rend null a déjà averti lui-même (fichier absent, illisible) : pas de second message,
                    // surtout pas un faux « non prise en charge ». Seul un chemin vide reste à signaler.
                    if (video is null) warn?.Invoke(BackgroundSupport.NoVideo(settings.Path));
                    else if (string.IsNullOrEmpty(settings.Path)) warn?.Invoke(BackgroundSupport.Missing(settings.Path ?? ""));
                    break;
                }
        }

        return ThemeBackground(theme);
    }

    /// <summary>Fond par défaut du thème : « Aurore » ou une couleur unie.</summary>
    private static IBackground ThemeBackground(ThemeDefinition theme) =>
        theme.DefaultBackground.Aurora
            ? new AuroraBackground()
            : new ColorBackground(theme.DefaultBackground.Color);
}
