using SkiaSharp;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Image en pixels : une chaîne par rangée, un caractère par pixel ; « . » = transparent. Chaque autre caractère
/// désigne une couleur de la palette. Le dessin est net : chaque pixel est un rectangle plein, sans anticrénelage.
/// </summary>
public sealed class Sprite
{
    /// <summary>Caractère d'un pixel transparent.</summary>
    public const char Transparent = '.';

    private readonly string[] _rows;
    private readonly Dictionary<char, SKColor> _palette;

    /// <summary>Crée une image. Toutes les rangées ont la même longueur et chaque caractère autre que « . » figure dans la palette.</summary>
    /// <exception cref="ArgumentException">Rangées de longueurs différentes, ou caractère absent de la palette.</exception>
    public Sprite(IReadOnlyList<string> rows, IReadOnlyDictionary<char, SKColor> palette)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(palette);

        _rows = [.. rows];
        _palette = new Dictionary<char, SKColor>(palette);
        Height = _rows.Length;
        Width = _rows.Length == 0 ? 0 : _rows[0].Length;

        for (var y = 0; y < _rows.Length; y++)
        {
            if (_rows[y].Length != Width)
            {
                throw new ArgumentException($"La rangée {y} fait {_rows[y].Length} pixels de large au lieu de {Width}.", nameof(rows));
            }

            foreach (var c in _rows[y])
            {
                if (c != Transparent && !_palette.ContainsKey(c))
                {
                    throw new ArgumentException($"Le caractère « {c} » (rangée {y}) ne figure pas dans la palette.", nameof(rows));
                }
            }
        }
    }

    /// <summary>Largeur en pixels de sprite.</summary>
    public int Width { get; }

    /// <summary>Hauteur en pixels de sprite.</summary>
    public int Height { get; }

    /// <summary>
    /// Dessine l'image, son coin haut gauche en (<paramref name="x"/>, <paramref name="y"/>), chaque pixel de sprite
    /// étant un carré de côté <paramref name="pixel"/>. <paramref name="alpha"/> s'ajoute à l'opacité de la palette.
    /// </summary>
    public void Draw(SKCanvas canvas, float x, float y, float pixel, byte alpha = 255)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (alpha == 0)
        {
            return;
        }

        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        for (var row = 0; row < _rows.Length; row++)
        {
            var line = _rows[row];
            var column = 0;
            while (column < line.Length)
            {
                var c = line[column];
                var end = column + 1;
                while (end < line.Length && line[end] == c)
                {
                    end++;
                }

                // Les pixels voisins de même couleur forment un seul rectangle : moins d'appels de dessin, aucune couture.
                if (c != Transparent)
                {
                    var color = _palette[c];
                    paint.Color = color.WithAlpha((byte)Math.Round(color.Alpha * alpha / 255.0));
                    canvas.DrawRect(x + column * pixel, y + row * pixel, (end - column) * pixel, pixel, paint);
                }

                column = end;
            }
        }
    }
}
