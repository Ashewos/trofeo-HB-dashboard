using SkiaSharp;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Particules des mascottes (confettis, cœurs, étincelles). Le tirage se fait à graine fixe : avec les mêmes paramètres,
/// chaque appel redonne exactement les mêmes trajectoires, donc le clip se répète à l'identique.
/// </summary>
internal static class Particles
{
    /// <summary>Un pixel plein de la couleur donnée.</summary>
    public static Sprite Dot(SKColor color) =>
        new(["d"], new Dictionary<char, SKColor> { ['d'] = color });

    /// <summary>
    /// Les calques de particules pour la pose <paramref name="step"/> (de 0 à <paramref name="steps"/> − 1) d'un clip.
    /// <paramref name="count"/> particules partent de <paramref name="origin"/> (coin haut gauche de l'image) vers une
    /// cible tirée dans <paramref name="area"/> ; la position suit <paramref name="ease"/> sur l'ensemble du clip, et
    /// chaque pose relie la position de son début à celle de sa fin (la pose elle-même reste en <see cref="Ease.Linear"/>).
    /// Les images de <paramref name="sprites"/> se répartissent à tour de rôle. Les cibles se répartissent en bandes
    /// verticales régulières (une par particule) pour que le jet soit étalé sans amas ; chaque particule disparaît avant
    /// la fin du clip, à un moment tiré lui aussi. L'image entière reste dans <paramref name="area"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Une image est plus grande que la zone.</exception>
    public static IReadOnlyList<Layer> Burst(
        IReadOnlyList<Sprite> sprites,
        int count,
        int seed,
        SKPointI origin,
        SKRectI area,
        int step,
        int steps,
        Ease ease)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentOutOfRangeException.ThrowIfZero(sprites.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(step);
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);

        var layers = new List<Layer>();
        var random = new Random(seed);
        var from = Math.Min(1f, (float)step / steps);
        var to = Math.Min(1f, (float)(step + 1) / steps);
        for (var i = 0; i < count; i++)
        {
            var sprite = sprites[i % sprites.Count];
            var maxX = area.Right - sprite.Width;
            var maxY = area.Bottom - sprite.Height;
            if (maxX < area.Left || maxY < area.Top)
            {
                throw new ArgumentException("Une image des particules ne tient pas dans la zone.", nameof(sprites));
            }

            // Bande verticale de la particule i : [lo, hi] dans la largeur utile (maxX - Left + 1 colonnes possibles).
            var columns = maxX - area.Left + 1;
            var lo = area.Left + columns * i / count;
            var hi = Math.Max(lo, area.Left + columns * (i + 1) / count - 1);

            // Les trois tirages ont lieu à chaque appel, même si la particule a disparu : la suite reste identique.
            var targetX = random.Next(lo, hi + 1);
            var targetY = random.Next(area.Top, maxY + 1);
            var life = Math.Max(1, steps - random.Next(0, 4));
            if (step >= life)
            {
                continue;
            }

            var target = new SKPointI(targetX, targetY);
            layers.Add(new Layer(sprite, PositionAt(origin, target, ease, from), PositionAt(origin, target, ease, to)));
        }

        return layers;
    }

    private static SKPointI PositionAt(SKPointI origin, SKPointI target, Ease ease, float t)
    {
        var e = (double)Easing.Apply(ease, t);
        return new SKPointI(
            (int)Math.Round(origin.X + (target.X - origin.X) * e, MidpointRounding.AwayFromZero),
            (int)Math.Round(origin.Y + (target.Y - origin.Y) * e, MidpointRounding.AwayFromZero));
    }
}
