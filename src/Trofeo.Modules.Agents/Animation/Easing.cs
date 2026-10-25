namespace Trofeo.Modules.Agents;

/// <summary>Courbe d'interpolation d'une pose.</summary>
public enum Ease
{
    Linear,
    InQuad,
    OutQuad,
    InOutQuad,

    /// <summary>Dépasse un peu l'arrivée puis revient : les valeurs peuvent dépasser 1.</summary>
    OutBack,
}

/// <summary>Calcul des courbes d'interpolation.</summary>
public static class Easing
{
    private const float C1 = 1.70158f;
    private const float C3 = C1 + 1f;

    /// <summary>Valeur de la courbe en <paramref name="t"/>, borné à [0, 1] ; 0 donne 0 et 1 donne 1 pour toutes les courbes.</summary>
    public static float Apply(Ease ease, float t)
    {
        // « !(t > 0) » couvre aussi NaN.
        if (!(t > 0f))
        {
            return 0f;
        }

        if (t >= 1f)
        {
            return 1f;
        }

        return ease switch
        {
            Ease.Linear => t,
            Ease.InQuad => t * t,
            Ease.OutQuad => 1f - (1f - t) * (1f - t),
            Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
            Ease.OutBack => 1f + C3 * MathF.Pow(t - 1f, 3f) + C1 * MathF.Pow(t - 1f, 2f),
            _ => throw new ArgumentOutOfRangeException(nameof(ease), ease, null),
        };
    }
}
