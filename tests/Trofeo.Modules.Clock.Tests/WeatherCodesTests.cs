using SkiaSharp;
using Trofeo.Modules.Clock;

namespace Trofeo.Modules.Clock.Tests;

public class WeatherCodesTests
{
    [Theory]
    [InlineData(0, true, WeatherIcon.Sun, "Ensoleillé")]
    [InlineData(0, false, WeatherIcon.Moon, "Dégagé")]
    [InlineData(1, true, WeatherIcon.PartlyCloudyDay, "Peu nuageux")]
    [InlineData(1, false, WeatherIcon.PartlyCloudyNight, "Peu nuageux")]
    [InlineData(2, true, WeatherIcon.PartlyCloudyDay, "Éclaircies")]
    [InlineData(2, false, WeatherIcon.PartlyCloudyNight, "Éclaircies")]
    [InlineData(3, true, WeatherIcon.Cloud, "Couvert")]
    [InlineData(3, false, WeatherIcon.Cloud, "Couvert")]
    [InlineData(45, true, WeatherIcon.Fog, "Brouillard")]
    [InlineData(48, true, WeatherIcon.Fog, "Brouillard")]
    [InlineData(51, true, WeatherIcon.Drizzle, "Bruine")]
    [InlineData(53, true, WeatherIcon.Drizzle, "Bruine")]
    [InlineData(55, true, WeatherIcon.Drizzle, "Bruine")]
    [InlineData(56, true, WeatherIcon.Drizzle, "Bruine verglaçante")]
    [InlineData(57, true, WeatherIcon.Drizzle, "Bruine verglaçante")]
    [InlineData(61, true, WeatherIcon.Rain, "Pluie")]
    [InlineData(63, true, WeatherIcon.Rain, "Pluie")]
    [InlineData(65, true, WeatherIcon.Rain, "Forte pluie")]
    [InlineData(66, true, WeatherIcon.Rain, "Pluie verglaçante")]
    [InlineData(67, true, WeatherIcon.Rain, "Pluie verglaçante")]
    [InlineData(71, true, WeatherIcon.Snow, "Neige")]
    [InlineData(73, true, WeatherIcon.Snow, "Neige")]
    [InlineData(75, true, WeatherIcon.Snow, "Forte neige")]
    [InlineData(77, true, WeatherIcon.Snow, "Grains de neige")]
    [InlineData(80, true, WeatherIcon.Rain, "Averses")]
    [InlineData(81, true, WeatherIcon.Rain, "Averses")]
    [InlineData(82, true, WeatherIcon.Rain, "Fortes averses")]
    [InlineData(85, true, WeatherIcon.Snow, "Averses de neige")]
    [InlineData(86, true, WeatherIcon.Snow, "Averses de neige")]
    [InlineData(95, true, WeatherIcon.Storm, "Orage")]
    [InlineData(96, true, WeatherIcon.Storm, "Orage et grêle")]
    [InlineData(99, true, WeatherIcon.Storm, "Orage et grêle")]
    [InlineData(4, true, WeatherIcon.Unknown, "—")]
    [InlineData(-1, false, WeatherIcon.Unknown, "—")]
    [InlineData(1000, true, WeatherIcon.Unknown, "—")]
    public void Describe_Table(int code, bool isDay, WeatherIcon icon, string label)
    {
        Assert.Equal((icon, label), WeatherCodes.Describe(code, isDay));
    }

    [Fact]
    public void WeatherIcons_AllGridsAre12By9()
    {
        foreach (var icon in Enum.GetValues<WeatherIcon>())
        {
            var grid = WeatherIcons.Grid(icon);
            Assert.Equal(9, grid.Length);
            Assert.All(grid, row => Assert.Equal(12, row.Length));
        }
    }

    [Fact]
    public void WeatherIcons_PartlyCloudyDay_MatchesMockupSvg()
    {
        // Rectangles du SVG td-wx : soleil (4,1,4,1) et (3,2,6,2) ; nuage (1,4,10,3), (2,3,4,1) et (0,5,12,2).
        var cells = new char[9][];
        for (var y = 0; y < 9; y++) cells[y] = new string('.', 12).ToCharArray();
        void Fill(int x, int y, int w, int h, char c)
        {
            for (var j = y; j < y + h; j++)
                for (var i = x; i < x + w; i++) cells[j][i] = c;
        }
        Fill(4, 1, 4, 1, 'S');
        Fill(3, 2, 6, 2, 'S');
        Fill(1, 4, 10, 3, 'C');
        Fill(2, 3, 4, 1, 'C');
        Fill(0, 5, 12, 2, 'C');

        Assert.Equal(cells.Select(row => new string(row)).ToArray(), WeatherIcons.Grid(WeatherIcon.PartlyCloudyDay));
    }

    [Theory]
    [InlineData(54f, 40.5f, 100f, 100f)]
    [InlineData(30f, 22.5f, 7.5f, 3.25f)]
    [InlineData(54f, 40f, 0f, 0f)]
    [InlineData(11f, 8f, 20f, 20f)]
    public void WeatherIcons_DrawInsideRect(float width, float height, float left, float top)
    {
        var rect = new SKRect(left, top, left + width, top + height);
        foreach (var icon in Enum.GetValues<WeatherIcon>())
        {
            var bg = new SKColor(0x10, 0x10, 0x10);
            using var bitmap = new SKBitmap(200, 200);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(bg);

            WeatherIcons.Draw(canvas, icon, rect);

            var inside = 0;
            var outside = 0;
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y) == bg) continue;
                    // Un pixel est dans le rectangle si sa case en recouvre au moins une partie.
                    if (x + 1 > rect.Left && x < rect.Right && y + 1 > rect.Top && y < rect.Bottom) inside++;
                    else outside++;
                }

            Assert.True(inside > 0, $"{icon} : aucun pixel peint");
            Assert.True(outside == 0, $"{icon} : {outside} pixels hors du rectangle");
        }
    }
}
