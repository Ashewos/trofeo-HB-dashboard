using SkiaSharp;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Fait vivre une mascotte : enchaîne des clips séparés de pauses au repos, puis l'endort après 10 min d'inactivité (30 min quand elle attend l'utilisateur).
/// Aucun minuteur interne : le temps est lu dans <see cref="TimeProvider"/> à chaque appel, et les enchaînements dus
/// depuis l'appel précédent sont rattrapés dans l'ordre. Utilisable depuis deux threads (<see cref="SetState"/> depuis le
/// sondage, le reste depuis le rendu) : l'état est protégé par un verrou, et le dessin se fait hors verrou.
/// </summary>
public sealed class MascotAnimator
{
    /// <summary>Durée en <see cref="MascotState.Idle"/> au bout de laquelle la mascotte s'endort.</summary>
    public static readonly TimeSpan SleepAfter = TimeSpan.FromMinutes(10);

    /// <summary>Durée en <see cref="MascotState.Waiting"/> au bout de laquelle la mascotte s'endort aussi (session oubliée).</summary>
    public static readonly TimeSpan WaitingSleepAfter = TimeSpan.FromMinutes(30);

    // Garde-fou : après une très longue suspension, on ne rattrape pas des milliers de clips un par un.
    private const int MaxCatchUpSteps = 10_000;

    private enum Phase
    {
        Clip,
        Pause,
        Sleep,
    }

    private readonly MascotDefinition _definition;
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly long _origin;
    private readonly Lock _lock = new();

    private MascotState _state = MascotState.Idle;
    private Phase _phase;
    private TimeSpan _phaseStart;
    // Fin de la phase ; TimeSpan.MaxValue pour une pause sans fin (état sans clip) et pour le sommeil.
    private TimeSpan _phaseEnd;
    private Clip? _clip;
    private string? _lastClipName;
    private TimeSpan _idleSince;
    // Début de l'attente de l'utilisateur en cours (sert au sommeil après WaitingSleepAfter).
    private TimeSpan _waitingSince;

    /// <summary>Crée l'animateur, au repos en <see cref="MascotState.Idle"/> (il commence par une pause).</summary>
    public MascotAnimator(MascotDefinition definition, TimeProvider time, Random random)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(random);
        if (definition.SceneWidth <= 0 || definition.SceneHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "La scène doit avoir une taille positive.");
        }

        _definition = definition;
        _time = time;
        _random = random;
        _origin = time.GetTimestamp();
        StartPause(TimeSpan.Zero);
    }

    /// <summary>État courant (<see cref="MascotState.Done"/> repasse seul à <see cref="MascotState.Idle"/> après son clip).</summary>
    public MascotState State
    {
        get
        {
            lock (_lock)
            {
                Advance(Now());
                return _state;
            }
        }
    }

    /// <summary>Vrai pendant un clip ; faux pendant une pause, au repos sans clip ou endormie.</summary>
    public bool IsAnimating
    {
        get
        {
            lock (_lock)
            {
                Advance(Now());
                return _phase == Phase.Clip;
            }
        }
    }

    /// <summary>Vrai quand la mascotte dort, jusqu'au prochain changement d'état.</summary>
    public bool IsSleeping
    {
        get
        {
            lock (_lock)
            {
                Advance(Now());
                return _phase == Phase.Sleep;
            }
        }
    }

    /// <summary>Nom du clip en cours, ou <c>null</c> hors clip.</summary>
    public string? CurrentClip
    {
        get
        {
            lock (_lock)
            {
                Advance(Now());
                return _phase == Phase.Clip ? _clip?.Name : null;
            }
        }
    }

    /// <summary>Change d'état : un clip du nouvel état démarre tout de suite et réveille la mascotte. Même état : sans effet.</summary>
    public void SetState(MascotState state)
    {
        lock (_lock)
        {
            var now = Now();
            Advance(now);
            if (state == _state)
            {
                return;
            }

            _state = state;
            if (state == MascotState.Idle)
            {
                _idleSince = now;
            }
            else if (state == MascotState.Waiting)
            {
                _waitingSince = now;
            }

            if (state == MascotState.Done && GetClips(MascotState.Done).Count == 0)
            {
                // Rien à jouer : « Done » est déjà fini, la mascotte passe tout de suite à Idle.
                _state = MascotState.Idle;
                _idleSince = now;
                StartPause(now);
                return;
            }

            StartClip(now);
        }
    }

    /// <summary>
    /// Délai avant que l'image change : la fin de la pause, ou de la pose en cours pendant un clip ; avant l'endormissement
    /// si celui-ci vient plus tôt ; <see cref="TimeSpan.MaxValue"/> si rien ne changera avant un changement d'état.
    /// </summary>
    public TimeSpan UntilNextChange()
    {
        lock (_lock)
        {
            var now = Now();
            Advance(now);
            if (_phase == Phase.Sleep)
            {
                return TimeSpan.MaxValue;
            }

            var next = _phase == Phase.Clip ? EndOfCurrentPose(now) : _phaseEnd;
            if (SleepAt() is { } sleepAt)
            {
                next = TimeSpan.FromTicks(Math.Min(next.Ticks, sleepAt.Ticks));
            }

            return next == TimeSpan.MaxValue ? next : TimeSpan.FromTicks(Math.Max(0, (next - now).Ticks));
        }
    }

    /// <summary>
    /// Dessine la scène dans <paramref name="area"/> : pixels de sprite entiers (au moins 1), scène centrée horizontalement
    /// et posée en bas, tout découpé par <paramref name="area"/>.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect area)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        Pose pose;
        float progress;
        lock (_lock)
        {
            var now = Now();
            Advance(now);
            (pose, progress) = CurrentPose(now);
        }

        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var pixel = MathF.Max(1f, MathF.Floor(MathF.Min(area.Width / _definition.SceneWidth, area.Height / _definition.SceneHeight)));
        var left = MathF.Round(area.Left + (area.Width - _definition.SceneWidth * pixel) / 2f);
        var top = MathF.Round(area.Bottom - _definition.SceneHeight * pixel);

        canvas.Save();
        try
        {
            canvas.ClipRect(area, SKClipOperation.Intersect, false);
            foreach (var layer in pose.Layers)
            {
                var position = layer.PositionAt(pose.Ease, progress);
                layer.Sprite.Draw(canvas, left + position.X * pixel, top + position.Y * pixel, pixel);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private TimeSpan Now() => _time.GetElapsedTime(_origin);

    private IReadOnlyList<Clip> GetClips(MascotState state) =>
        _definition.Clips.TryGetValue(state, out var clips) ? clips : [];

    // Rattrape, dans l'ordre, les enchaînements dus jusqu'à « now ». Appelée sous verrou.
    private void Advance(TimeSpan now)
    {
        for (var step = 0; ; step++)
        {
            if (_phase == Phase.Sleep)
            {
                return;
            }

            if (SleepAt() is { } sleepAt && now >= sleepAt)
            {
                _phase = Phase.Sleep;
                _clip = null;
                _phaseStart = sleepAt;
                _phaseEnd = TimeSpan.MaxValue;
                return;
            }

            if (_phaseEnd > now)
            {
                return;
            }

            var at = step < MaxCatchUpSteps ? _phaseEnd : now;
            if (_phase == Phase.Clip)
            {
                if (_state == MascotState.Done)
                {
                    // Le clip de fin joue une seule fois, puis la mascotte revient au calme.
                    _state = MascotState.Idle;
                    _idleSince = at;
                }

                StartPause(at);
            }
            else
            {
                StartClip(at);
            }
        }
    }

    // Heure d'endormissement de l'état courant : après SleepAfter en Idle, WaitingSleepAfter en Waiting, jamais en Working.
    private TimeSpan? SleepAt() => _state switch
    {
        MascotState.Idle => _idleSince + SleepAfter,
        MascotState.Waiting => _waitingSince + WaitingSleepAfter,
        _ => null,
    };

    private void StartPause(TimeSpan at)
    {
        var clips = GetClips(_state);
        _phase = Phase.Pause;
        _clip = null;
        _phaseStart = at;
        _phaseEnd = clips.Count == 0 ? TimeSpan.MaxValue : at + PauseFor(_state);
    }

    private void StartClip(TimeSpan at)
    {
        var clips = GetClips(_state);
        if (clips.Count == 0)
        {
            // Aucun clip pour cet état : la mascotte reste au repos, sans animer.
            _phase = Phase.Pause;
            _clip = null;
            _phaseStart = at;
            _phaseEnd = TimeSpan.MaxValue;
            return;
        }

        _clip = PickClip(clips);
        _lastClipName = _clip.Name;
        _phase = Phase.Clip;
        _phaseStart = at;
        _phaseEnd = at + _clip.Duration;
    }

    // Un autre clip que le précédent dès qu'il y a au moins 2 variantes (de noms différents).
    private Clip PickClip(IReadOnlyList<Clip> clips)
    {
        var candidates = clips.Where(c => c.Name != _lastClipName).ToList();
        if (candidates.Count == 0)
        {
            return clips[_random.Next(clips.Count)];
        }

        return candidates[_random.Next(candidates.Count)];
    }

    private TimeSpan PauseFor(MascotState state)
    {
        var (min, max) = state switch
        {
            MascotState.Working => (500, 2000),
            MascotState.Waiting => (1000, 3000),
            _ => (4000, 12000),
        };
        return TimeSpan.FromMilliseconds(_random.Next(min, max + 1));
    }

    // Fin de la pose en cours dans le clip en cours (toujours après « now » : Advance a tout rattrapé).
    private TimeSpan EndOfCurrentPose(TimeSpan now)
    {
        if (_clip is null)
        {
            return _phaseEnd;
        }

        var end = _phaseStart;
        foreach (var pose in _clip.Poses)
        {
            end += TimeSpan.FromMilliseconds(Math.Max(0, pose.DurationMs));
            if (end > now)
            {
                return end;
            }
        }

        return _phaseEnd;
    }

    private (Pose Pose, float Progress) CurrentPose(TimeSpan now)
    {
        switch (_phase)
        {
            case Phase.Sleep:
                return (_definition.Sleep, 0f);
            case Phase.Clip when _clip is not null:
                var start = _phaseStart;
                foreach (var pose in _clip.Poses)
                {
                    var duration = TimeSpan.FromMilliseconds(Math.Max(0, pose.DurationMs));
                    if (start + duration > now)
                    {
                        return (pose, (float)((now - start) / duration));
                    }

                    start += duration;
                }

                return (_definition.Rest, 0f);
            default:
                return (_definition.Rest, 0f);
        }
    }
}
