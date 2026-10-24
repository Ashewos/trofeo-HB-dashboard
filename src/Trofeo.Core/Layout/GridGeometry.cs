using SkiaSharp;

namespace Trofeo.Core.Layout;

/// <summary>Géométrie du damier 16 × 4 de l'écran 1920 × 462.</summary>
public static class GridGeometry
{
    public const int Width = 1920;
    public const int Height = 462;
    public const int Columns = 16;
    public const int Rows = 4;
    public const int Inset = 14;
    public const int Gap = 12;

    // Zone intérieure plus un espace (1892 + 12, 434 + 12) : chaque module perd un espace à droite/en bas.
    private const int InnerWidth = Width - 2 * Inset + Gap;
    private const int InnerHeight = Height - 2 * Inset + Gap;

    /// <summary>Rectangle en pixels d'un module placé sur le damier.</summary>
    public static SKRectI ModuleRect(int column, int row, int columns, int rows)
    {
        int x0 = Inset + Snap(column, InnerWidth, Columns);
        int x1 = Inset + Snap(column + columns, InnerWidth, Columns) - Gap;
        int y0 = Inset + Snap(row, InnerHeight, Rows);
        int y1 = Inset + Snap(row + rows, InnerHeight, Rows) - Gap;
        return new SKRectI(x0, y0, x1, y1);
    }

    private static int Snap(int index, int extent, int divisions) =>
        (int)Math.Round((double)index * extent / divisions, MidpointRounding.AwayFromZero);
}
