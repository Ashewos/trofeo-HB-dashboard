using SkiaSharp;

namespace Trofeo.ModuleKit;

/// <summary>
/// Forme du thème. <see cref="Rounded"/> : coins arrondis, barres pleines, points ronds.
/// <see cref="Pixel"/> : angles vifs, barres en segments, points carrés, onglet actif marqué d'un trait de 4 px à gauche
/// (chaque module le met en œuvre lui-même).
/// </summary>
public enum ThemeShape { Rounded, Pixel }

/// <summary>Palette et typographie communes à tous les modules.</summary>
public sealed record Theme(
    string Id,
    SKColor Background,
    SKColor Panel,
    float PanelOpacity,
    float CornerRadius,
    SKColor Text,
    SKColor TextMuted,
    SKColor Accent,
    SKColor Warning,
    SKTypeface Typeface)
{
    // Les valeurs par défaut dérivent d'autres propriétés (Text, Typeface) : champs nullables privés,
    // résolus à la lecture. Ainsi « with { Text = … } » entraîne les propriétés jamais renseignées.
    private SKColor? _highlight;
    private SKColor? _surface;
    private SKColor? _track;
    private SKTypeface? _feedTypeface;
    private float? _feedScale;

    /// <summary>Forme du thème ; défaut <see cref="ThemeShape.Rounded"/>.</summary>
    public ThemeShape Shape { get; init; }

    /// <summary>Titres mis en avant ; défaut : <see cref="Text"/>.</summary>
    public SKColor Highlight { get => _highlight ?? Text; init => _highlight = value; }

    /// <summary>Onglet actif, zones en relief ; défaut : <see cref="Text"/> à 7 % (alpha 18).</summary>
    public SKColor Surface { get => _surface ?? Text.WithAlpha(18); init => _surface = value; }

    /// <summary>Piste des barres ; défaut : <see cref="Text"/> à 8 % (alpha 20).</summary>
    public SKColor Track { get => _track ?? Text.WithAlpha(20); init => _track = value; }

    /// <summary>Police du fil d'activité ; défaut : <see cref="Typeface"/>.</summary>
    public SKTypeface FeedTypeface { get => _feedTypeface ?? Typeface; init => _feedTypeface = value; }

    /// <summary>Agrandissement du fil d'activité ; défaut 1.</summary>
    public float FeedScale { get => _feedScale ?? 1f; init => _feedScale = value; }
}
