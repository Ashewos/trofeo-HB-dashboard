using SkiaSharp;

namespace Trofeo.Core.Rendering;

/// <summary>
/// Lisse dans le temps l'opacité voulue d'un panneau sur un fond animé. La garde de lisibilité mesure la zone la plus claire
/// sous le panneau : une petite tache claire qui passe dessous ferait sauter l'opacité de tout le panneau d'une image à la
/// suivante. L'opacité appliquée suit donc la cible par un lissage exponentiel : vite quand elle monte (le texte doit rester
/// lisible), lentement quand elle descend (le panneau ne doit pas clignoter). L'état est gardé par rectangle de panneau.
/// Un seul thread (celui de l'affichage).
/// </summary>
internal sealed class PanelAlphaSmoother
{
    /// <summary>Constante de temps quand l'opacité monte : environ 1 s pour rattraper la cible.</summary>
    public static readonly TimeSpan RiseTime = TimeSpan.FromSeconds(0.4);

    /// <summary>Constante de temps quand l'opacité descend : environ 15 s pour rattraper la cible.</summary>
    public static readonly TimeSpan FallTime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Plus grand écart de temps pris en compte entre deux images : un saut d'horloge ou une absence de l'écran ne fait pas de bond.
    /// </summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(1);

    private sealed class State(float alpha, DateTimeOffset time)
    {
        public float Alpha = alpha;
        public DateTimeOffset Time = time;
    }

    private readonly Dictionary<SKRect, State> _states = [];

    /// <summary>
    /// L'opacité à appliquer à <paramref name="now"/> pour le panneau <paramref name="rect"/>, quand la garde en veut
    /// <paramref name="target"/>. La première image d'un panneau prend directement la cible ; un même instant, ou une horloge
    /// qui recule, ne change rien.
    /// </summary>
    public float Smooth(SKRect rect, float target, DateTimeOffset now)
    {
        if (!_states.TryGetValue(rect, out var state))
        {
            _states[rect] = new State(target, now);
            return target;
        }

        var step = now - state.Time;
        state.Time = now; // une horloge qui recule repart de sa nouvelle valeur
        if (step <= TimeSpan.Zero) return state.Alpha;
        if (step > MaxStep) step = MaxStep;

        var timeConstant = target > state.Alpha ? RiseTime : FallTime;
        float keep = MathF.Exp((float)(-step.TotalSeconds / timeConstant.TotalSeconds));
        state.Alpha = target + (state.Alpha - target) * keep;
        return state.Alpha;
    }
}
