using SkiaSharp;
using Trofeo.Core.Layout;

namespace Trofeo.Core.Tests;

public class GridGeometryTests
{
    [Theory]
    [InlineData(0, 0, 8, 4, 14, 14, 954, 448)]     // Claude Code
    [InlineData(8, 0, 4, 2, 966, 14, 1430, 225)]   // Monitoring
    [InlineData(8, 2, 4, 2, 966, 237, 1430, 448)]  // En écoute
    [InlineData(12, 0, 4, 2, 1442, 14, 1906, 225)] // Heure
    [InlineData(12, 2, 4, 2, 1442, 237, 1906, 448)] // Réseau
    public void ModuleRect_AlexLayout(int column, int row, int columns, int rows,
        int left, int top, int right, int bottom)
    {
        Assert.Equal(new SKRectI(left, top, right, bottom), GridGeometry.ModuleRect(column, row, columns, rows));
    }

    [Fact]
    public void ModuleRect_Pixel_Spacing()
    {
        var pixel = new GridSpacing(18, 20);

        Assert.Equal(new SKRectI(18, 18, 1902, 444), GridGeometry.ModuleRect(0, 0, 16, 4, pixel));

        var left = GridGeometry.ModuleRect(0, 0, 8, 4, pixel);
        var right = GridGeometry.ModuleRect(8, 0, 8, 4, pixel);
        Assert.Equal(20, right.Left - left.Right);
        var top = GridGeometry.ModuleRect(0, 0, 16, 2, pixel);
        var bottom = GridGeometry.ModuleRect(0, 2, 16, 2, pixel);
        Assert.Equal(20, bottom.Top - top.Bottom);
    }

    [Fact]
    public void ModuleRect_DefaultOverload_Unchanged()
    {
        Assert.Equal(new GridSpacing(14, 12), GridSpacing.Default);
        Assert.Equal(
            GridGeometry.ModuleRect(8, 2, 4, 2, GridSpacing.Default),
            GridGeometry.ModuleRect(8, 2, 4, 2));
    }

    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(1919f, 461f, 15, 3)]
    [InlineData(-50f, 900f, 0, 3)]
    [InlineData(900f, 250f, 7, 2)]
    public void CellAt_MapsAndClamps(float x, float y, int col, int row)
    {
        Assert.Equal((col, row), GridGeometry.CellAt(x, y, GridSpacing.Default));
    }

    [Fact]
    public void CellAt_PointInGap_BelongsToNearestCell()
    {
        // Colonne 0 : x de 14 à 121, écart de 121 à 133, milieu de l'écart à 127.
        Assert.Equal(0, GridGeometry.CellAt(125f, 0f, GridSpacing.Default).Column);
        Assert.Equal(1, GridGeometry.CellAt(129f, 0f, GridSpacing.Default).Column);
        // Rangée 0 : y de 14 à 114, écart de 114 à 126, milieu à 120.
        Assert.Equal(0, GridGeometry.CellAt(0f, 119f, GridSpacing.Default).Row);
        Assert.Equal(1, GridGeometry.CellAt(0f, 121f, GridSpacing.Default).Row);
    }

    [Fact]
    public void CellAt_UsesTheThemeSpacing()
    {
        var pixel = new GridSpacing(18, 20);
        var right = GridGeometry.ModuleRect(8, 0, 8, 4, pixel);
        var center = GridGeometry.CellAt((right.Left + right.Right) / 2f, (right.Top + right.Bottom) / 2f, pixel);
        Assert.Equal(12, center.Column);
        Assert.Equal(2, center.Row);

        // Juste avant le début du module de droite, dans l'écart : le milieu de l'écart sépare les deux cases.
        var left = GridGeometry.ModuleRect(0, 0, 8, 4, pixel);
        Assert.Equal(7, GridGeometry.CellAt(left.Right + 1, 10f, pixel).Column);
        Assert.Equal(8, GridGeometry.CellAt(right.Left - 1, 10f, pixel).Column);
    }

    [Fact]
    public void NearestColumnEdge_SnapsToClosestBoundary()
    {
        var spacing = GridSpacing.Default;
        // Bord 0 à x = 14, bord 1 au milieu de l'écart (127), bord 16 à x = 1906.
        Assert.Equal(0, GridGeometry.NearestColumnEdge(14f, spacing));
        Assert.Equal(0, GridGeometry.NearestColumnEdge(70.4f, spacing)); // un peu avant la mi-chemin 0/1
        Assert.Equal(1, GridGeometry.NearestColumnEdge(70.6f, spacing)); // un peu après
        Assert.Equal(1, GridGeometry.NearestColumnEdge(127f, spacing));
        Assert.Equal(8, GridGeometry.NearestColumnEdge(960f, spacing));
        Assert.Equal(16, GridGeometry.NearestColumnEdge(1919f, spacing));
        Assert.Equal(16, GridGeometry.NearestColumnEdge(5000f, spacing));
        Assert.Equal(0, GridGeometry.NearestColumnEdge(-30f, spacing));
    }

    [Fact]
    public void NearestRowEdge_SnapsToClosestBoundary()
    {
        var spacing = GridSpacing.Default;
        // Bord 0 à y = 14, bord 1 à 120, bord 2 à 231, bord 4 à y = 448.
        Assert.Equal(0, GridGeometry.NearestRowEdge(0f, spacing));
        Assert.Equal(1, GridGeometry.NearestRowEdge(110f, spacing));
        Assert.Equal(2, GridGeometry.NearestRowEdge(231f, spacing));
        Assert.Equal(4, GridGeometry.NearestRowEdge(461f, spacing));
    }
}
