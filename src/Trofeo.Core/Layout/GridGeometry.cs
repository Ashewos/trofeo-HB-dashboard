using SkiaSharp;

namespace Trofeo.Core.Layout;

/// <summary>Marge autour du damier et écart entre deux modules, en pixels (propres à chaque thème).</summary>
public readonly record struct GridSpacing(int Inset, int Gap)
{
    /// <summary>14 px de marge, 12 px d'écart : Classique et Liquid Glass.</summary>
    public static GridSpacing Default { get; } = new(14, 12);
}

/// <summary>Géométrie du damier 16 × 4 de l'écran 1920 × 462.</summary>
public static class GridGeometry
{
    public const int Width = 1920;
    public const int Height = 462;
    public const int Columns = 16;
    public const int Rows = 4;
    public const int Inset = 14;
    public const int Gap = 12;

    /// <summary>Rectangle en pixels d'un module placé sur le damier, avec les écarts par défaut.</summary>
    public static SKRectI ModuleRect(int column, int row, int columns, int rows) =>
        ModuleRect(column, row, columns, rows, GridSpacing.Default);

    /// <summary>Rectangle en pixels d'un module placé sur le damier, avec les écarts du thème.</summary>
    public static SKRectI ModuleRect(int column, int row, int columns, int rows, GridSpacing spacing)
    {
        // Zone intérieure plus un espace (ex. 1892 + 12, 434 + 12) : chaque module perd un espace à droite/en bas.
        int innerWidth = Width - 2 * spacing.Inset + spacing.Gap;
        int innerHeight = Height - 2 * spacing.Inset + spacing.Gap;
        int x0 = spacing.Inset + Snap(column, innerWidth, Columns);
        int x1 = spacing.Inset + Snap(column + columns, innerWidth, Columns) - spacing.Gap;
        int y0 = spacing.Inset + Snap(row, innerHeight, Rows);
        int y1 = spacing.Inset + Snap(row + rows, innerHeight, Rows) - spacing.Gap;
        return new SKRectI(x0, y0, x1, y1);
    }

    private static int Snap(int index, int extent, int divisions) =>
        (int)Math.Round((double)index * extent / divisions, MidpointRounding.AwayFromZero);
}
