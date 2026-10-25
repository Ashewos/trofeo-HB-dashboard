using SkiaSharp;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Un calque d'une pose : une image et sa position en pixels de sprite, depuis le coin haut gauche de la scène.
/// Pendant la pose, elle va de <paramref name="From"/> à <paramref name="To"/>.
/// </summary>
public sealed record Layer(Sprite Sprite, SKPointI From, SKPointI To)
{
    /// <summary>
    /// Position à l'avancement <paramref name="t"/> (0 à 1) de la pose : <c>From + (To − From) × Ease(t)</c>, arrondie au
    /// pixel de sprite entier le plus proche (0,5 s'éloigne de zéro). Les déplacements restent nets, sans glissement doux.
    /// </summary>
    internal SKPointI PositionAt(Ease ease, float t)
    {
        var e = (double)Easing.Apply(ease, t);
        return new SKPointI(Lerp(From.X, To.X, e), Lerp(From.Y, To.Y, e));
    }

    private static int Lerp(int from, int to, double e) =>
        (int)Math.Round(from + (to - from) * e, MidpointRounding.AwayFromZero);
}

/// <summary>Une image fixe ou en mouvement de la scène, pendant <paramref name="DurationMs"/>.</summary>
public sealed record Pose(int DurationMs, IReadOnlyList<Layer> Layers, Ease Ease = Ease.Linear);

/// <summary>Une séquence de poses, jouée d'un bout à l'autre.</summary>
public sealed record Clip(string Name, IReadOnlyList<Pose> Poses)
{
    /// <summary>Durée totale du clip.</summary>
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Poses.Sum(p => Math.Max(0, p.DurationMs)));
}

/// <summary>État de la mascotte, qui suit l'état de la session.</summary>
public enum MascotState
{
    Idle,
    Working,
    Waiting,
    Done,
}

/// <summary>
/// Une mascotte : sa scène (en pixels de sprite), la pose de repos, la pose de sommeil et, par état, les variantes de clips.
/// Un état sans clip laisse la mascotte au repos, sans animation.
/// </summary>
public sealed record MascotDefinition(
    string Name,
    int SceneWidth,
    int SceneHeight,
    Pose Rest,
    Pose Sleep,
    IReadOnlyDictionary<MascotState, IReadOnlyList<Clip>> Clips);
