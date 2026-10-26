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

    /// <summary>
    /// Case sous un point de l'écran (pixels 1920 × 462, non tourné), bornée au damier. Un point dans un écart entre
    /// deux modules appartient à la case la plus proche.
    /// </summary>
    public static (int Column, int Row) CellAt(float x, float y, GridSpacing spacing) =>
        (CellIndex(x, Width, Columns, spacing), CellIndex(y, Height, Rows, spacing));

    /// <summary>Bord de colonne (0 à 16) le plus proche d'un point : sert au redimensionnement.</summary>
    public static int NearestColumnEdge(float x, GridSpacing spacing) => NearestEdge(x, Width, Columns, spacing);

    /// <summary>Bord de rangée (0 à 4) le plus proche d'un point : sert au redimensionnement.</summary>
    public static int NearestRowEdge(float y, GridSpacing spacing) => NearestEdge(y, Height, Rows, spacing);

    /// <summary>Position en pixels d'un bord : au milieu de l'écart entre deux cases, au bord du module aux extrémités.</summary>
    private static double EdgePosition(int edge, int screen, int divisions, GridSpacing spacing)
    {
        int inner = screen - 2 * spacing.Inset + spacing.Gap;
        if (edge == 0) return spacing.Inset;
        if (edge == divisions) return spacing.Inset + Snap(divisions, inner, divisions) - spacing.Gap;
        return spacing.Inset + Snap(edge, inner, divisions) - spacing.Gap / 2.0;
    }

    private static int CellIndex(float coordinate, int screen, int divisions, GridSpacing spacing)
    {
        // La case k commence au bord k (milieu de l'écart qui la précède).
        var index = 0;
        for (var k = 1; k < divisions; k++)
            if (coordinate >= EdgePosition(k, screen, divisions, spacing)) index = k;
        return index;
    }

    private static int NearestEdge(float coordinate, int screen, int divisions, GridSpacing spacing)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var k = 0; k <= divisions; k++)
        {
            var distance = Math.Abs(coordinate - EdgePosition(k, screen, divisions, spacing));
            if (distance < bestDistance) // égalité : le bord le plus petit
            {
                best = k;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int Snap(int index, int extent, int divisions) =>
        (int)Math.Round((double)index * extent / divisions, MidpointRounding.AwayFromZero);
}
