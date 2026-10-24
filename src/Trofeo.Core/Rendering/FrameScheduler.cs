namespace Trofeo.Core.Rendering;

/// <summary>
/// Décide quand envoyer une image : seulement si quelque chose a changé (ou s'anime)
/// et jamais plus souvent que le plafond d'images par seconde.
/// </summary>
public sealed class FrameScheduler
{
    private readonly TimeProvider _time;
    private int _dirty;
    private bool _hasRendered;
    private long _lastRenderTimestamp;

    /// <param name="time">Source de temps (mockable en test).</param>
    /// <param name="maxFps">Plafond d'images par seconde ; doit être strictement positif.</param>
    public FrameScheduler(TimeProvider time, double maxFps = 12)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (!(maxFps > 0) || double.IsInfinity(maxFps))
        {
            throw new ArgumentOutOfRangeException(nameof(maxFps), maxFps, "Le plafond d'images par seconde doit être strictement positif.");
        }

        _time = time;
        MinInterval = TimeSpan.FromTicks((long)Math.Round(TimeSpan.TicksPerSecond / maxFps));
    }

    /// <summary>Durée minimale entre deux envois (1000 / maxFps ms).</summary>
    public TimeSpan MinInterval { get; }

    /// <summary>Signale qu'un module doit être redessiné. Peut être appelé depuis n'importe quel thread.</summary>
    public void MarkDirty() => Volatile.Write(ref _dirty, 1);

    /// <summary>
    /// Vrai s'il faut rendre une image maintenant. Quand la réponse est vraie, la demande de
    /// redessin en attente est consommée : un <see cref="MarkDirty"/> arrivé pendant le rendu
    /// reste donc en attente pour l'image suivante. À appeler depuis le thread de la boucle.
    /// </summary>
    public bool ShouldRender(bool anyAnimating)
    {
        bool wanted = anyAnimating || Volatile.Read(ref _dirty) != 0;
        if (!wanted)
        {
            return false;
        }

        if (_hasRendered && _time.GetElapsedTime(_lastRenderTimestamp) < MinInterval)
        {
            return false;
        }

        Interlocked.Exchange(ref _dirty, 0);
        return true;
    }

    /// <summary>À appeler une fois l'image envoyée : démarre l'intervalle minimum.</summary>
    public void OnRendered()
    {
        _lastRenderTimestamp = _time.GetTimestamp();
        _hasRendered = true;
    }
}
