namespace Trofeo.Settings.Services;

/// <summary>
/// Les avertissements d'apparence d'un rechargement, rangés sous le réglage qu'ils concernent. Le service les rend en
/// vrac (<c>Reloaded.Warnings</c>) : on les reconnaît à leur texte, qui vient de <c>AppearanceResolver</c>, <c>FontResolver</c>,
/// <c>BackgroundFactory</c> et du lecteur de vidéo.
/// </summary>
public sealed record AppearanceWarnings(
    IReadOnlyList<string> Theme, IReadOnlyList<string> Opacity, IReadOnlyList<string> Font, IReadOnlyList<string> Background)
{
    public static AppearanceWarnings Empty { get; } = new([], [], [], []);

    public bool IsEmpty => Theme.Count + Opacity.Count + Font.Count + Background.Count == 0;
}

public static class WarningRouter
{
    /// <summary>Range chaque avertissement sous son réglage ; ce qui n'est pas d'apparence (raccourcis, modules…) reste à part.</summary>
    public static (AppearanceWarnings Appearance, IReadOnlyList<string> Other) Route(IEnumerable<string> warnings)
    {
        List<string> theme = [], opacity = [], font = [], background = [], other = [];
        foreach (var warning in warnings)
        {
            var bucket = warning switch
            {
                _ when warning.StartsWith("Thème «", StringComparison.Ordinal) => theme,
                _ when warning.StartsWith("Opacité", StringComparison.Ordinal) => opacity,
                _ when warning.StartsWith("Police «", StringComparison.Ordinal) => font,
                _ when warning.StartsWith("Fond d'écran", StringComparison.Ordinal)
                    || warning.StartsWith("Couleur de fond", StringComparison.Ordinal)
                    || warning.Contains("vidéo", StringComparison.OrdinalIgnoreCase) => background,
                _ => other,
            };
            bucket.Add(warning);
        }

        return (new AppearanceWarnings(theme, opacity, font, background), other);
    }
}
