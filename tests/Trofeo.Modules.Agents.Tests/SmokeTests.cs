using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Modules.Agents.Tests;

public class SmokeTests
{
    [Fact]
    public void Skia_DrawsOnLinux()
    {
        using var bitmap = new SKBitmap(10, 10);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }

        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 5));
        Assert.NotNull(Themes.Classique.Typeface);
    }
}
