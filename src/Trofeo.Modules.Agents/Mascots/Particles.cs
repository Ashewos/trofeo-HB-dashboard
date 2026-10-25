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
    /// Les images de <paramref name="sprites"/> se répartissent à tour de rôle. La zone est découpée en une bande
    /// verticale par particule (l'image tient dans sa bande quand elle est assez étroite, donc deux voisines ne se
    /// chevauchent pas à l'arrivée), et les bandes paires et impaires se partagent le haut et le bas de la zone. Chaque
    /// particule disparaît avant la fin du clip, à un moment tiré lui aussi. La cible, donc l'arrivée du clip, tient
    /// toujours dans <paramref name="area"/> ; le départ et le trajet n'y tiennent que si <paramref name="origin"/> y
    /// tient aussi.
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

            // Bande verticale de la particule i : tout le x de la zone est partagé en « count » bandes. L'image se place
            // dans sa bande (ou à son bord gauche si la bande est plus étroite qu'elle), sans sortir de la zone.
            var stripLeft = area.Left + area.Width * i / count;
            var stripRight = area.Left + area.Width * (i + 1) / count;
            var lo = Math.Min(stripLeft, maxX);
            var hi = Math.Max(lo, Math.Min(stripRight - sprite.Width, maxX));

            // Bandes alternées en hauteur : les paires dans la moitié haute, les impaires dans la moitié basse.
            var yLo = area.Top;
            var yHi = maxY;
            if (maxY > area.Top)
            {
                var middle = area.Top + (maxY - area.Top + 1) / 2;
                if (i % 2 == 0)
                {
                    yHi = middle - 1;
                }
                else
                {
                    yLo = middle;
                }
            }

            // Les trois tirages ont lieu à chaque appel, même si la particule a disparu : la suite reste identique.
            var targetX = random.Next(lo, hi + 1);
            var targetY = random.Next(yLo, yHi + 1);
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
