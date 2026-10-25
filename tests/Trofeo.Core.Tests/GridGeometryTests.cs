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
}
