using System.Text.Json;
using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Le module d'un outil de code à agent (Claude Code, Codex) : les sessions ouvertes en onglets, le fil d'activité ou la vue
/// d'ensemble de la session affichée, une barre de chiffres (contexte, durée, modèle, quotas) et la mascotte.
/// <para>
/// La source est sondée toutes les secondes sur le minuteur du module, jamais deux sondages à la fois ; une commande de
/// raccourci (thread de l'interface) en lance un tout de suite sur le pool de threads. Le résultat est remplacé d'un bloc :
/// le rendu lit une photo immuable. La mascotte n'est changée d'état que par le sondage.
/// </para>
/// </summary>
public sealed class AgentModule(ModuleDescriptor descriptor, AgentFlavor flavor, IAgentSource source,
    AgentSettings settings, TimeProvider time, IModuleLog log, Random random) : IModule, ICommandableModule
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Après un choix à la main, le suivi automatique se tait pendant ce délai.</summary>
    private static readonly TimeSpan ManualHold = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan MinWake = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxWake = TimeSpan.FromHours(1);

    private const int FailuresBeforeOffline = 3;

    // « NaN » ne se sérialise pas par défaut : la signature ne doit jamais lever à cause d'un quota malformé.
    private static readonly JsonSerializerOptions SignatureOptions = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>Ce que le rendu lit : le dernier relevé et l'onglet affiché, publiés ensemble.</summary>
    private sealed record Shown(AgentSnapshot? Snapshot, string? SelectedId);

    private readonly MascotAnimator _animator = new(flavor.Mascot, time, random);
    private readonly Lock _gate = new();
    private Shown _shown = new(null, null);
    private ModuleStatus _status = new(ModuleHealth.Online);
    private string? _signature;
    private DateTimeOffset? _manualAt;
    private Dictionary<string, AgentActivity> _previousActivities = new(StringComparer.Ordinal);
    private (string Id, AgentActivity Activity)? _mascotSource;
    private int _failures;
    private int _polling;
    private int _pollRequested;
    private int _sourceDisposed;
    private bool _started;
    private bool _disposed;
    private ITimer? _pollTimer;
    private ITimer? _wakeTimer;

    public ModuleDescriptor Descriptor { get; } = descriptor;

    public ModuleStatus Status => Volatile.Read(ref _status);

    /// <summary>Vrai pendant un clip de la mascotte : l'hôte redessine alors au rythme des images.</summary>
    public bool IsAnimating => _animator.IsAnimating;

    public event EventHandler? Invalidated;

    public event EventHandler? StatusChanged;

    /// <summary>Session de l'onglet affiché, ou <c>null</c> (rien relevé, ou aucune session).</summary>
    internal string? SelectedSessionId => Volatile.Read(ref _shown).SelectedId;

    /// <summary>État de la mascotte.</summary>
    internal MascotState MascotState => _animator.State;

    /// <summary>Vrai quand la mascotte dort (image fixe, plus de minuteur).</summary>
    internal bool MascotSleeping => _animator.IsSleeping;

    /// <summary>Dernier relevé de la source, ou <c>null</c> avant le premier.</summary>
    internal AgentSnapshot? Snapshot => Volatile.Read(ref _shown).Snapshot;

    /// <summary>Le dessinateur du module (les tests y recueillent les textes dessinés).</summary>
    internal AgentPainter Painter { get; } = new();

    /// <summary>Lance un travail hors du thread appelant (remplaçable par les tests).</summary>
    internal Action<Action> RunInBackground { get; init; } = static work => ThreadPool.QueueUserWorkItem(static w => w(), work, preferLocal: false);

    public async Task StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;
        }

        var pollTimer = time.CreateTimer(_ => PollOnce(), null, PollInterval, PollInterval);
        var wakeTimer = time.CreateTimer(_ => OnWake(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var disposedMeanwhile = false;
        lock (_gate)
        {
            if (_disposed)
            {
                disposedMeanwhile = true;
            }
            else
            {
                _pollTimer = pollTimer;
                _wakeTimer = wakeTimer;
            }
        }

        if (disposedMeanwhile)
        {
            pollTimer.Dispose();
            wakeTimer.Dispose();
            return;
        }

        // Premier sondage tout de suite, hors du thread appelant : l'écran n'attend pas une seconde pour afficher quelque chose.
        await Task.Run(PollOnce, CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose()
    {
        ITimer? pollTimer;
        ITimer? wakeTimer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pollTimer = _pollTimer;
            wakeTimer = _wakeTimer;
            _pollTimer = null;
            _wakeTimer = null;
        }

        pollTimer?.Dispose();
        wakeTimer?.Dispose();

        // La source est libérée tout de suite si aucun sondage ne court, sinon par le sondage qui se termine.
        if (Interlocked.CompareExchange(ref _polling, 1, 0) == 0)
        {
            DisposeSource();
        }
    }

    // --- Commandes ---

    /// <inheritdoc />
    public bool HandleCommand(ModuleCommand command)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var shown = _shown;
            var sessions = shown.Snapshot?.Sessions;
            if (sessions is null || sessions.Count == 0)
            {
                return false;
            }

            var count = sessions.Count;
            var current = IndexOf(sessions, shown.SelectedId);
            int target;
            switch (command.Kind)
            {
                case ModuleCommandKind.NextTab:
                    target = (current + 1) % count;
                    break;
                case ModuleCommandKind.PreviousTab:
                    target = current < 0 ? count - 1 : (current + count - 1) % count;
                    break;
                case ModuleCommandKind.SelectTab when command.Index >= 0 && command.Index < count:
                    target = command.Index;
                    break;
                default:
                    return false;
            }

            _shown = shown with { SelectedId = sessions[target].Id };
            _manualAt = time.GetUtcNow();
        }

        Raise(Invalidated);

        // Le détail de l'onglet choisi arrive avec ce sondage, sans attendre le tick. Si un sondage court déjà (il a demandé
        // l'ancien onglet), le drapeau le fait recommencer à sa fin.
        Volatile.Write(ref _pollRequested, 1);
        RunInBackground(PollOnce);
        return true;
    }

    // --- Sondage ---

    /// <summary>Un sondage synchrone, pour les tests.</summary>
    internal void PollNow() => PollOnce();

    /// <summary>
    /// Un sondage, ignoré si un autre court encore ; s'il est demandé par une commande pendant un sondage en cours, ce dernier
    /// est refait à sa fin (avec le nouvel onglet). Ne lève jamais.
    /// </summary>
    private void PollOnce()
    {
        do
        {
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (Volatile.Read(ref _disposed))
                {
                    return;
                }

                // Remis à zéro avant de lire l'onglet : une commande arrivée ensuite le remet à un et fait refaire un sondage.
                Volatile.Write(ref _pollRequested, 0);
                PollCore();
            }
            finally
            {
                Volatile.Write(ref _polling, 0);

                // Libéré pendant le sondage : c'est à lui de libérer la source (la place reste prise, plus personne ne sondera).
                if (Volatile.Read(ref _disposed) && Interlocked.CompareExchange(ref _polling, 1, 0) == 0)
                {
                    DisposeSource();
                }
            }
        }
        while (Volatile.Read(ref _pollRequested) != 0 && !Volatile.Read(ref _disposed));
    }

    private void PollCore()
    {
        var now = time.GetUtcNow();
        AgentSnapshot? snapshot = null;
        try
        {
            snapshot = source.Poll(Volatile.Read(ref _shown).SelectedId, now);
        }
        catch (Exception ex)
        {
            log.Error($"Lecture de l'état de {Descriptor.DisplayName} impossible : {ex.Message}");
        }

        bool statusChanged, changed;
        try
        {
            (statusChanged, changed) = snapshot is null ? RegisterFailure() : Publish(snapshot, now);
        }
        catch (Exception ex)
        {
            // Une erreur de notre côté (et non de la source) : journalisée, sans compter comme une panne de la source.
            log.Error($"Mise à jour de {Descriptor.DisplayName} impossible : {ex.Message}");
            (statusChanged, changed) = (false, false);
        }

        // Un abonné qui lève ne doit ni faire perdre l'autre événement ni empêcher de réarmer le réveil.
        if (statusChanged)
        {
            Raise(StatusChanged);
        }

        if (changed)
        {
            Raise(Invalidated);
        }

        ArmWake();
    }

    /// <summary>Lève un événement ; l'exception d'un abonné est journalisée et ne remonte pas.</summary>
    private void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            log.Error($"Un abonné de {Descriptor.DisplayName} a échoué : {ex.Message}");
        }
    }

    /// <summary>Publie un relevé réussi : onglet, état du module, mascotte et signature. Rend ce qui a changé.</summary>
    private (bool StatusChanged, bool Changed) Publish(AgentSnapshot snapshot, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return (false, false);
            }

            _failures = 0;
            var selectedId = ChooseTab(snapshot, now);
            _shown = new Shown(snapshot, selectedId);

            var status = snapshot.Installed
                ? new ModuleStatus(ModuleHealth.Online)
                : new ModuleStatus(ModuleHealth.NotConfigured, flavor.NotInstalledText);
            var statusChanged = !Equals(_status, status);
            Volatile.Write(ref _status, status);

            UpdateMascot(snapshot, selectedId);

            var signature = Signature(snapshot, selectedId, status, now);
            var changed = signature != _signature;
            _signature = signature;
            return (statusChanged, changed);
        }
    }

    private (bool StatusChanged, bool Changed) RegisterFailure()
    {
        lock (_gate)
        {
            if (_disposed || ++_failures < FailuresBeforeOffline)
            {
                return (false, false);
            }

            var status = new ModuleStatus(ModuleHealth.Offline);
            if (Equals(_status, status))
            {
                return (false, false);
            }

            Volatile.Write(ref _status, status);
            return (true, true);
        }
    }

    // --- Onglet affiché ---

    /// <summary>
    /// Choisit l'onglet : l'actuel s'il existe encore, sauf si le suivi automatique désigne une session qui vient de passer
    /// à l'attente ; à défaut, la session à l'activité la plus récente. Appelée sous le verrou.
    /// </summary>
    private string? ChooseTab(AgentSnapshot snapshot, DateTimeOffset now)
    {
        var sessions = snapshot.Sessions;
        var chosen = _shown.SelectedId;

        if (settings.AutoFollow && (_manualAt is not { } manual || now - manual >= ManualHold))
        {
            AgentSessionSummary? waiting = null;
            foreach (var session in sessions)
            {
                // Seule une session déjà connue qui change d'état compte : au premier relevé, rien ne « passe » à l'attente.
                if (session.Activity == AgentActivity.Waiting
                    && _previousActivities.TryGetValue(session.Id, out var before) && before != AgentActivity.Waiting
                    && (waiting is null || session.LastActivityAt >= waiting.LastActivityAt))
                {
                    waiting = session;
                }
            }

            if (waiting is not null)
            {
                chosen = waiting.Id;
            }
        }

        var activities = new Dictionary<string, AgentActivity>(sessions.Count, StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            activities[session.Id] = session.Activity;
        }

        _previousActivities = activities;

        if (chosen is not null && IndexOf(sessions, chosen) >= 0)
        {
            return chosen;
        }

        AgentSessionSummary? latest = null;
        foreach (var session in sessions)
        {
            if (latest is null || session.LastActivityAt >= latest.LastActivityAt)
            {
                latest = session;
            }
        }

        return latest?.Id;
    }

    private static int IndexOf(IReadOnlyList<AgentSessionSummary> sessions, string? id)
    {
        if (id is null)
        {
            return -1;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    // --- Mascotte ---

    /// <summary>
    /// La mascotte suit l'activité de la session affichée ; la même session qui passe de « travaille » à « inactive » joue
    /// le clip de fin. Appelée depuis le sondage seulement.
    /// </summary>
    private void UpdateMascot(AgentSnapshot snapshot, string? selectedId)
    {
        var index = IndexOf(snapshot.Sessions, selectedId);
        if (index < 0)
        {
            _mascotSource = null;
            _animator.SetState(MascotState.Idle);
            return;
        }

        var session = snapshot.Sessions[index];
        var wasWorking = _mascotSource is { } previous && previous.Id == session.Id && previous.Activity == AgentActivity.Working;
        _mascotSource = (session.Id, session.Activity);

        var state = session.Activity switch
        {
            AgentActivity.Working => MascotState.Working,
            AgentActivity.Waiting => MascotState.Waiting,
            _ => wasWorking ? MascotState.Done : MascotState.Idle,
        };

        // Un sondage de plus, toujours inactif, ne doit pas couper le clip de fin.
        if (state == MascotState.Idle && _animator.State == MascotState.Done)
        {
            return;
        }

        _animator.SetState(state);
    }

    /// <summary>
    /// Arme le minuteur à un coup qui réveille le module quand l'image de la mascotte doit changer (fin de pause, de pose
    /// ou endormissement). Rien à armer quand plus rien ne changera avant un nouvel état.
    /// </summary>
    private void ArmWake()
    {
        ITimer? timer;
        lock (_gate)
        {
            timer = _wakeTimer;
        }

        if (timer is null)
        {
            return;
        }

        var delay = _animator.UntilNextChange();
        try
        {
            timer.Change(delay == TimeSpan.MaxValue ? Timeout.InfiniteTimeSpan : (delay < MinWake ? MinWake : delay > MaxWake ? MaxWake : delay), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Le module a été libéré entre-temps : plus rien à réveiller.
        }
    }

    private void OnWake()
    {
        Raise(Invalidated);
        ArmWake();
    }

    // --- Signature ---

    /// <summary>
    /// Sérialisation JSON de ce qui s'affiche, avec la minute de la durée : le module ne demande une image que si elle change.
    /// </summary>
    private static string Signature(AgentSnapshot snapshot, string? selectedId, ModuleStatus status, DateTimeOffset now)
    {
        var detail = snapshot.Selected;
        var taskMinutes = detail?.TaskStartedAt is { } startedAt ? (long)Math.Max(0, (now - startedAt).TotalMinutes) : (long?)null;
        var quotas = snapshot.Quotas;
        return JsonSerializer.Serialize(new
        {
            Selected = selectedId,
            Health = status.Health,
            status.ActionMessage,
            snapshot.Installed,
            Sessions = snapshot.Sessions.Select(s => new { s.Id, s.ProjectName, s.Activity }),
            Detail = detail,
            TaskMinutes = taskMinutes,
            FiveHour = SignatureOf(quotas?.FiveHour, now),
            Weekly = SignatureOf(quotas?.Weekly, now),
        }, SignatureOptions);
    }

    private static object? SignatureOf(QuotaWindow? window, DateTimeOffset now) =>
        window is null ? null : new { window.UsedPercent, window.ResetsAt, Expired = window.ResetsAt <= now };

    // --- Libération ---

    private void DisposeSource()
    {
        if (Interlocked.Exchange(ref _sourceDisposed, 1) == 0)
        {
            source.Dispose();
        }
    }

    // --- Dessin ---

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var shown = Volatile.Read(ref _shown);
        Painter.Draw(canvas, ctx, new AgentScene(shown.Snapshot, shown.SelectedId, Status, settings.View, flavor, _animator));
    }
}
