using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>Charge les polices embarquées dans l'assembly Trofeo.Core.</summary>
public static class FontLoader
{
    private const string Prefix = "Trofeo.Core.Assets.Fonts.";

    /// <summary>Charge une police par son nom de fichier (ex. <c>BarlowSemiCondensed-Regular.ttf</c>).</summary>
    /// <exception cref="FileNotFoundException">La ressource n'existe pas ou n'est pas une police valide.</exception>
    public static SKTypeface Load(string resourceName)
    {
        using var stream = typeof(FontLoader).Assembly.GetManifestResourceStream(Prefix + resourceName)
            ?? throw new FileNotFoundException($"Police embarquée introuvable : {resourceName}", resourceName);

        return SKTypeface.FromStream(stream)
            ?? throw new FileNotFoundException($"Police embarquée illisible : {resourceName}", resourceName);
    }
}
