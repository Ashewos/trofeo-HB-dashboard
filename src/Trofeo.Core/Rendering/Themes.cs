using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Rendering;

/// <summary>Thèmes fournis avec l'application.</summary>
public static class Themes
{
    private static readonly Lazy<Theme> ClassiqueTheme = new(() => new Theme(
        Id: "classique",
        Background: SKColor.Parse("#0E1116"),
        Panel: SKColor.Parse("#111317"),
        PanelOpacity: 0.86f,
        CornerRadius: 14f,
        Text: SKColor.Parse("#ECEEF0"),
        TextMuted: SKColor.Parse("#8C929C"),
        Accent: SKColor.Parse("#7FD1C7"),
        Warning: SKColor.Parse("#F2B84B"),
        Typeface: FontLoader.Load("BarlowSemiCondensed-Regular.ttf")));

    /// <summary>Thème par défaut : fond sombre, accent turquoise, Barlow Semi Condensed.</summary>
    public static Theme Classique => ClassiqueTheme.Value;
}
