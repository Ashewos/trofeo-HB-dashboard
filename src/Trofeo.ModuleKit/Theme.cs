using SkiaSharp;

namespace Trofeo.ModuleKit;

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
    SKTypeface Typeface);
