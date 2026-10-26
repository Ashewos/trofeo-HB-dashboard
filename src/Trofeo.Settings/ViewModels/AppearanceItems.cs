using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.Core.Rendering;

namespace Trofeo.Settings.ViewModels;

/// <summary>Une carte de thème : sa vignette (une image fixe faite par ThemePreview), son nom et sa phrase.</summary>
public sealed partial class ThemeCard : ObservableObject
{
    private Bitmap? _thumbnail;

    public ThemeCard(string id, string name, string description, Func<Task> select)
    {
        Id = id;
        Name = name;
        Description = description;
        SelectCommand = new AsyncRelayCommand(select);
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>La vignette embarquée (<c>Assets/Themes/&lt;id&gt;.png</c>) ; lue au premier affichage. <c>null</c> si elle manque.</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnail is not null)
                return _thumbnail;
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://Trofeo.Settings/Assets/Themes/{Id}.png"));
                return _thumbnail = new Bitmap(stream);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                return null; // une vignette absente ne doit pas empêcher de choisir le thème
            }
        }
    }
}

/// <summary>
/// Une ligne de la liste des polices : « Celle du thème », une police embarquée, le séparateur ou une police du système.
/// Chaque nom est écrit dans sa propre police.
/// </summary>
public sealed class FontItem
{
    private const string EmbeddedRoot = "avares://Trofeo.Settings/Assets/Fonts#";

    /// <param name="name">Ce que la liste affiche.</param>
    /// <param name="value">La famille à écrire dans le fichier ; <c>null</c> : celle du thème.</param>
    public FontItem(string name, string? value, bool isSeparator = false, bool isThemeDefault = false)
    {
        Name = name;
        Value = value;
        IsSeparator = isSeparator;
        IsThemeDefault = isThemeDefault;
        FontSize = 15 * EmbeddedFonts.ScaleOf(name);
        FontFamily = isSeparator || isThemeDefault
            ? FontFamily.Default
            : new FontFamily(EmbeddedFonts.Families.Contains(name, StringComparer.OrdinalIgnoreCase) ? EmbeddedRoot + name : name);
    }

    public string Name { get; }
    public string? Value { get; }
    public bool IsSeparator { get; }
    public bool IsThemeDefault { get; }
    public bool IsChoice => !IsSeparator;
    public FontFamily FontFamily { get; }

    /// <summary>15 px, agrandi comme l'écran le fait pour une police qui dessine petit (VT323).</summary>
    public double FontSize { get; }
}

/// <summary>Une sorte de fond (Celui du thème, Couleur, Photo, GIF, Vidéo) dans la rangée de boutons.</summary>
public sealed partial class BackgroundKindItem : ObservableObject
{
    public BackgroundKindItem(BackgroundKind kind, string label, Func<Task> select)
    {
        Kind = kind;
        Label = label;
        SelectCommand = new AsyncRelayCommand(select);
    }

    public BackgroundKind Kind { get; }
    public string Label { get; }
    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Une pastille de la palette de couleurs du fond.</summary>
public sealed partial class SwatchItem : ObservableObject
{
    public SwatchItem(string hex, Func<Task> select)
    {
        Hex = hex;
        Brush = new SolidColorBrush(Color.Parse(hex));
        SelectCommand = new AsyncRelayCommand(select);
    }

    /// <summary>« #RRGGBB », en majuscules.</summary>
    public string Hex { get; }
    public IBrush Brush { get; }
    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}
