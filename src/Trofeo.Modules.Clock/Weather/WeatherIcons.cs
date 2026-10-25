using SkiaSharp;

namespace Trofeo.Modules.Clock;

/// <summary>
/// Icônes météo en pixel art sur une grille 12 × 9, dessinées en rectangles. Chaque lettre de la grille
/// désigne une couleur : <c>S</c> soleil et éclair, <c>C</c> nuage, <c>R</c> pluie, <c>W</c> neige,
/// <c>M</c> lune, <c>F</c> brouillard ; le point est transparent.
/// </summary>
public static class WeatherIcons
{
    internal const int Columns = 12;
    internal const int Rows = 9;

    private static readonly SKColor Sun = new(0xFF, 0xD2, 0x7A);
    private static readonly SKColor Cloud = new(0xE9, 0xEE, 0xF5);
    private static readonly SKColor Rain = new(0x7F, 0xB8, 0xFF);
    private static readonly SKColor Snow = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor Moon = new(0xD9, 0xDC, 0xE8);
    private static readonly SKColor Fog = new(0xAE, 0xB4, 0xBE);

    /// <summary>Le haut d'un nuage, commun aux icônes de précipitations.</summary>
    private static readonly string[] CloudTop =
    [
        "....CCCC....",
        "..CCCCCCCC..",
        ".CCCCCCCCCC.",
        "CCCCCCCCCCCC",
        ".CCCCCCCCCC.",
    ];

    private static readonly string[] SunGrid =
    [
        ".....SS.....",
        ".S...SS...S.",
        "..S.SSSS.S..",
        "...SSSSSS...",
        "SS.SSSSSS.SS",
        "...SSSSSS...",
        "..S.SSSS.S..",
        ".S...SS...S.",
        ".....SS.....",
    ];

    private static readonly string[] MoonGrid =
    [
        "............",
        "....MMMMM...",
        "...MMMM.....",
        "..MMMM......",
        "..MMMM......",
        "..MMMM......",
        "...MMMM...M.",
        "....MMMMMM..",
        "............",
    ];

    // Soleil et nuage : exactement le SVG td-wx de la maquette.
    private static readonly string[] PartlyCloudyDayGrid =
    [
        "............",
        "....SSSS....",
        "...SSSSSS...",
        "..CCCCSSS...",
        ".CCCCCCCCCC.",
        "CCCCCCCCCCCC",
        "CCCCCCCCCCCC",
        "............",
        "............",
    ];

    private static readonly string[] PartlyCloudyNightGrid =
    [
        "............",
        "....MMMM....",
        "...MMM......",
        "..CCCCM.....",
        ".CCCCCCCCCC.",
        "CCCCCCCCCCCC",
        "CCCCCCCCCCCC",
        "............",
        "............",
    ];

    private static readonly string[] CloudGrid =
    [
        "............",
        "....CCCC....",
        "..CCCCCCCC..",
        ".CCCCCCCCCC.",
        "CCCCCCCCCCCC",
        "CCCCCCCCCCCC",
        ".CCCCCCCCCC.",
        "............",
        "............",
    ];

    private static readonly string[] FogGrid =
    [
        "............",
        "..FFFFFFFF..",
        "............",
        ".FFFFFFFFFF.",
        "............",
        "..FFFFFFFF..",
        "............",
        ".FFFFFFFFFF.",
        "............",
    ];

    private static readonly string[] DrizzleGrid = [.. CloudTop, "............", "..R...R...R.", "............", ".R...R...R.."];
    private static readonly string[] RainGrid = [.. CloudTop, ".R..R..R..R.", ".R..R..R..R.", "..R..R..R...", "..R..R..R..."];
    private static readonly string[] SnowGrid = [.. CloudTop, ".W...W...W..", "............", "...W...W...W", "............"];
    private static readonly string[] StormGrid = [.. CloudTop, ".....SSS....", "....SSS.....", "...SSSSS....", "......S....."];

    private static readonly string[] UnknownGrid =
    [
        "............",
        "....FFFF....",
        "...FF..FF...",
        ".......FF...",
        "......FF....",
        ".....FF.....",
        ".....FF.....",
        "............",
        ".....FF.....",
    ];

    /// <summary>Grille de l'icône : 9 rangées de 12 caractères.</summary>
    internal static string[] Grid(WeatherIcon icon) => icon switch
    {
        WeatherIcon.Sun => SunGrid,
        WeatherIcon.Moon => MoonGrid,
        WeatherIcon.PartlyCloudyDay => PartlyCloudyDayGrid,
        WeatherIcon.PartlyCloudyNight => PartlyCloudyNightGrid,
        WeatherIcon.Cloud => CloudGrid,
        WeatherIcon.Fog => FogGrid,
        WeatherIcon.Drizzle => DrizzleGrid,
        WeatherIcon.Rain => RainGrid,
        WeatherIcon.Snow => SnowGrid,
        WeatherIcon.Storm => StormGrid,
        _ => UnknownGrid,
    };

    /// <summary>
    /// Côté d'une case de la grille pour tenir dans <paramref name="width"/> × <paramref name="height"/> :
    /// arrondi au demi-pixel inférieur pour garder des carrés nets, exact sous 1 pixel.
    /// </summary>
    internal static float CellSize(float width, float height)
    {
        var cell = Math.Min(width / Columns, height / Rows);
        return cell >= 1f ? MathF.Floor(cell * 2f) / 2f : Math.Max(cell, 0f);
    }

    /// <summary>Dessine l'icône, centrée dans <paramref name="rect"/> et sans jamais en sortir.</summary>
    public static void Draw(SKCanvas canvas, WeatherIcon icon, SKRect rect)
    {
        var cell = CellSize(rect.Width, rect.Height);
        if (cell <= 0f) return;

        var left = rect.Left + (rect.Width - cell * Columns) / 2f;
        var top = rect.Top + (rect.Height - cell * Rows) / 2f;
        var grid = Grid(icon);

        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var y = 0; y < Rows; y++)
        {
            var row = grid[y];
            for (var x = 0; x < Columns;)
            {
                var letter = row[x];
                var end = x + 1;
                while (end < Columns && row[end] == letter) end++; // une seule case pour une suite de même couleur

                if (ColorOf(letter) is { } color)
                {
                    paint.Color = color;
                    canvas.DrawRect(left + x * cell, top + y * cell, (end - x) * cell, cell, paint);
                }
                x = end;
            }
        }
    }

    private static SKColor? ColorOf(char letter) => letter switch
    {
        'S' => Sun,
        'C' => Cloud,
        'R' => Rain,
        'W' => Snow,
        'M' => Moon,
        'F' => Fog,
        _ => null,
    };
}
