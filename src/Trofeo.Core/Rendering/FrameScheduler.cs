namespace Trofeo.Core.Rendering;

/// <summary>
/// Décide quand envoyer une image : seulement si quelque chose a changé (ou s'anime)
/// et jamais plus souvent que le plafond d'images par seconde.
/// </summary>
public sealed class FrameScheduler : IDisposable
{
    private readonly TimeProvider _time;
    private readonly AutoResetEvent _signal = new(false);
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

    /// <summary>
    /// Levé par <see cref="MarkDirty"/> et <see cref="Wake"/> : la boucle d'affichage dort dessus
    /// (avec un délai) au lieu de se réveiller à intervalle fixe. Se réarme seul à chaque réveil.
    /// </summary>
    public WaitHandle Signal => _signal;

    /// <summary>Vrai si un redessin est en attente (sans le consommer).</summary>
    public bool IsDirty => Volatile.Read(ref _dirty) != 0;

    /// <summary>Signale qu'un module doit être redessiné et réveille la boucle. Peut être appelé depuis n'importe quel thread.</summary>
    public void MarkDirty()
    {
        Volatile.Write(ref _dirty, 1);
        Wake();
    }

    /// <summary>Réveille la boucle sans demander de redessin. Peut être appelé depuis n'importe quel thread.</summary>
    public void Wake()
    {
        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            // Un module en fin de vie peut signaler après l'arrêt : sans conséquence.
        }
    }

    /// <summary>
    /// Délai avant que l'intervalle minimum entre deux images soit écoulé : 0 si aucune image n'a encore été
    /// rendue, sinon <c>max(0, MinInterval − écoulé)</c>. À appeler depuis le thread de la boucle.
    /// </summary>
    public TimeSpan UntilNextSlot()
    {
        if (!_hasRendered)
        {
            return TimeSpan.Zero;
        }

        var remaining = MinInterval - _time.GetElapsedTime(_lastRenderTimestamp);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Vrai s'il faut rendre une image maintenant. Quand la réponse est vraie, la demande de
    /// redessin en attente est consommée : un <see cref="MarkDirty"/> arrivé pendant le rendu
    /// reste donc en attente pour l'image suivante. À appeler depuis le thread de la boucle.
    /// </summary>
    public bool ShouldRender(bool anyAnimating)
    {
        bool wanted = anyAnimating || IsDirty;
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

    public void Dispose() => _signal.Dispose();
}
