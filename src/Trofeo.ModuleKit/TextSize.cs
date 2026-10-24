namespace Trofeo.ModuleKit;

/// <summary>Taille du texte réglable par module.</summary>
public enum TextSize { Small, Normal, Large }

public static class TextSizeExtensions
{
    /// <summary>Facteur multiplicatif : 0.85 / 1.0 / 1.2.</summary>
    public static float Factor(this TextSize s) => s switch
    {
        TextSize.Small => 0.85f,
        TextSize.Normal => 1.0f,
        TextSize.Large => 1.2f,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };
}
