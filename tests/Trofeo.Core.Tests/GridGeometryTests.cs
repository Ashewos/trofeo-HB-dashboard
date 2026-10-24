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
}
