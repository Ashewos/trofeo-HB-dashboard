using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>Les polices embarquées dans l'assembly (licence OFL, texte à côté des fichiers).</summary>
public static class EmbeddedFonts
{
    public const string Barlow = "Barlow Semi Condensed", Manrope = "Manrope", Pixelify = "Pixelify Sans", Vt323 = "VT323";

    // Une seule graisse par famille. Barlow : la Regular, celle que le thème Classique utilise.
    private static readonly (string Family, string Resource)[] Sources =
    {
        (Barlow, "BarlowSemiCondensed-Regular.ttf"),
        (Manrope, "Manrope-Medium.ttf"),
        (Pixelify, "PixelifySans-Regular.ttf"),
        (Vt323, "VT323-Regular.ttf"),
    };

    private static readonly Dictionary<string, Lazy<SKTypeface>> Cache = Sources.ToDictionary(
        s => s.Family,
        s => new Lazy<SKTypeface>(() => FontLoader.Load(s.Resource)),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Les quatre familles, dans cet ordre.</summary>
    public static IReadOnlyList<string> Families { get; } = Sources.Select(s => s.Family).ToArray();

    /// <summary>Police embarquée par nom de famille (casse ignorée) ; null si ce n'en est pas une, ou si sa ressource manque ou est illisible. Chargée une fois, gardée.</summary>
    public static SKTypeface? Find(string family)
    {
        if (family is null || !Cache.TryGetValue(family, out var face))
            return null;

        // Une ressource absente ou illisible ne doit jamais faire planter le service : la police est
        // alors traitée comme absente (le Lazy garde l'exception, chaque appel rend donc null).
        try
        {
            return face.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Polices installées sur le PC. Remplaçable en test.</summary>
public interface ISystemFonts
{
    /// <summary>Familles installées, triées, sans doublon (casse ignorée).</summary>
    IReadOnlyList<string> Families { get; }

    /// <summary>Police installée de cette famille ; null si absente.</summary>
    SKTypeface? Match(string family);
}

/// <summary><c>SKFontManager.Default</c>. <see cref="Match"/> rend null quand Skia renvoie une police d'une autre famille (repli silencieux de Skia).</summary>
public sealed class SkiaSystemFonts : ISystemFonts
{
    public IReadOnlyList<string> Families { get; } = SKFontManager.Default.GetFontFamilies()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public SKTypeface? Match(string family)
    {
        var face = SKFontManager.Default.MatchFamily(family);
        if (face is null)
            return null;
        if (string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            return face;

        face.Dispose();
        return null;
    }
}

/// <summary>Trouve la police demandée par l'utilisateur, ou à défaut celle du thème.</summary>
public static class FontResolver
{
    /// <summary>
    /// Ordre : null ou blanc donne <paramref name="fallback"/> ; puis police embarquée ; puis police du système ;
    /// sinon <paramref name="fallback"/>, et <paramref name="warn"/> reçoit « Police « {family} » introuvable : police du thème utilisée. ».
    /// </summary>
    public static SKTypeface Resolve(string? family, SKTypeface fallback, ISystemFonts system, Action<string>? warn = null)
    {
        if (string.IsNullOrWhiteSpace(family))
            return fallback;

        var name = family.Trim();
        var face = EmbeddedFonts.Find(name) ?? system.Match(name);
        if (face is not null)
            return face;

        warn?.Invoke($"Police « {family} » introuvable : police du thème utilisée.");
        return fallback;
    }
}
