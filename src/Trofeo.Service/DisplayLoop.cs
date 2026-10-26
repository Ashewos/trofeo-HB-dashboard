using System.Diagnostics;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Device;
using Trofeo.ModuleKit;

namespace Trofeo.Display;

/// <summary>
/// La boucle d'affichage : reconnecte l'écran quand il est absent, puis rend et envoie une image
/// quand un module en a demandé une. Possède les modules qu'elle crée, ainsi que le fond et le compositeur d'images ;
/// <see cref="Tick"/> s'appelle depuis un seul thread.
/// </summary>
public sealed class DisplayLoop : IDisposable
{
    private const string ResumeReason = "Reprise après veille";
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(1);

    /// <summary>Plus longue attente renvoyée par <see cref="Tick"/> : la boucle se réveille au moins une fois par seconde.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// L'écran repasse au logo Thermalright environ 3 s après la dernière image reçue : sans nouvelle image,
    /// on lui renvoie la dernière (déjà encodée) à ce rythme.
    /// </summary>
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(1);

    private readonly DashboardLayout _layout;
    private readonly ScreenDevice _screen;
    private readonly FrameScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly FrameComposer _composer;
    private readonly ThemeDefinition _theme;
    private readonly IBackground _background;
    private readonly long _backgroundStart;
    private TimeSpan? _backgroundDue;       // échéance absolue de l'image suivante du fond, sur l'axe du temps du fond (depuis _backgroundStart)
    private TimeSpan? _backgroundRetryAt;   // fond en échec : prochain essai d'avance, sur le même axe
    private readonly RateLimitedErrorLog _renderErrors;
    private readonly Dictionary<Guid, IModule> _modules = [];
    private readonly IModule[] _moduleList;

    // Identifiants d'instance, remplis par le constructeur seul puis lus depuis n'importe quel thread (jamais modifiés ensuite).
    private readonly Dictionary<IModule, Guid> _instanceIds = [];
    private readonly EventHandler _onInvalidated;
    private readonly EventHandler _onStatusChanged;
    private bool _hasTriedConnect;
    private long _lastConnectAttempt;
    private long _lastPresenceCheck;
    private long _lastSend;
    private bool _forceReconnect;
    private int _reconnectRequested;
    private bool _disposed;

    // Durées de rendu (composition et encodage) et d'envoi des images envoyées depuis la dernière ligne de journal.
    private TimeSpan _renderTotal;
    private TimeSpan _sendTotal;
    private int _timedFrames;

    /// <param name="theme">Le thème complet ; Classique si absent.</param>
    /// <param name="background">
    /// Le fond de l'écran ; la boucle en devient propriétaire et le libère à son arrêt. Absent : fond uni de la couleur du thème.
    /// </param>
    public DisplayLoop(
        DashboardLayout layout, ModuleRegistry registry, ScreenDevice screen, FrameScheduler scheduler, TimeProvider time,
        ThemeDefinition? theme = null, IBackground? background = null)
    {
        _layout = layout;
        _screen = screen;
        _scheduler = scheduler;
        _time = time;
        _onInvalidated = (_, _) => _scheduler.MarkDirty();
        _onStatusChanged = OnStatusChanged;
        _renderErrors = new RateLimitedErrorLog(time);
        _composer = new FrameComposer(_renderErrors);
        _theme = theme ?? Themes.ClassiqueDefinition;
        _background = background ?? new ColorBackground(_theme.DefaultBackground.Color);
        _backgroundStart = time.GetTimestamp(); // la lecture d'un fond animé part du démarrage de la boucle

        foreach (var placement in layout.Placements)
        {
            try
            {
                AddModule(layout, registry, placement, time);
            }
            catch (Exception ex)
            {
                // Une disposition mal formée ne doit jamais empêcher le service de démarrer.
                Trace.TraceError($"Module ignoré : emplacement {placement?.InstanceId} refusé : {ex}");
            }
        }

        _moduleList = [.. _modules.Values];
        _scheduler.MarkDirty(); // la première image part dès que l'écran est prêt
    }

    private void AddModule(DashboardLayout layout, ModuleRegistry registry, ModulePlacement placement, TimeProvider time)
    {
        var error = LayoutValidator.Check(layout, placement, registry.Descriptors);
        if (error != PlacementError.None)
        {
            Trace.TraceWarning($"Module ignoré : « {placement.TypeId} » ({placement.InstanceId}) refusé : {error}.");
            return;
        }

        var module = registry.Create(placement, time);
        if (module is null) return;

        if (_modules.ContainsKey(placement.InstanceId))
        {
            // Deux emplacements du même identifiant : le second ne doit pas écraser (ni laisser tourner) le premier.
            Trace.TraceWarning($"Module ignoré : identifiant d'instance en double ({placement.InstanceId}).");
            module.Dispose();
            return;
        }

        _instanceIds[module] = placement.InstanceId;
        module.Invalidated += _onInvalidated;
        module.StatusChanged += _onStatusChanged;
        _modules[placement.InstanceId] = module;
    }

    /// <summary>
    /// Le module qui reçoit une commande de raccourci : l'instance demandée si elle accepte les commandes, sinon le
    /// premier module qui les accepte, dans l'ordre de la disposition (colonne, puis rangée). <c>null</c> s'il n'y en a aucun.
    /// Appelable depuis le thread de l'interface : <c>_modules</c> n'est modifié que par le constructeur et <see cref="Dispose"/>,
    /// et l'appelant doit cesser d'appeler avant de libérer la boucle.
    /// </summary>
    public ICommandableModule? FindCommandTarget(Guid? instanceId)
    {
        if (instanceId is { } id && _modules.TryGetValue(id, out var requested) && requested is ICommandableModule target)
        {
            return target;
        }

        foreach (var placement in _layout.Placements.OrderBy(p => p.Column).ThenBy(p => p.Row))
        {
            if (_modules.TryGetValue(placement.InstanceId, out var module) && module is ICommandableModule commandable)
            {
                return commandable;
            }
        }

        return null;
    }

    /// <summary>Journalise le nouvel état d'un module et redemande une image. Peut être appelé depuis n'importe quel thread.</summary>
    private void OnStatusChanged(object? sender, EventArgs e)
    {
        if (sender is IModule module)
        {
            try
            {
                var status = module.Status;
                var action = status.ActionMessage is { Length: > 0 } message ? $" — {message}" : "";
                var instance = _instanceIds.TryGetValue(module, out var id) ? $" {id.ToString("N")[..8]}" : "";
                Trace.TraceInformation($"[{module.Descriptor.TypeId}{instance}] État : {status.Health}{action}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Échec de lecture de l'état d'un module : {ex.Message}");
            }
        }

        _scheduler.MarkDirty();
    }

    /// <summary>Nombre d'images envoyées et validées par l'écran depuis le démarrage.</summary>
    public int FramesSent { get; private set; }

    /// <summary>Nombre de renvois de la dernière image (écran maintenu allumé sans changement).</summary>
    public int KeepAlivesSent { get; private set; }

    /// <summary>Démarre les modules. Un module qui échoue est journalisé, les autres continuent.</summary>
    public void Start(CancellationToken ct)
    {
        foreach (var module in _moduleList)
        {
            try
            {
                module.StartAsync(ct).ContinueWith(
                    t => Trace.TraceError($"Échec du démarrage du module « {module.Descriptor.TypeId} » : {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Échec du démarrage du module « {module.Descriptor.TypeId} » : {ex}");
            }
        }
    }

    /// <summary>
    /// Demande d'abandonner la connexion et de se reconnecter tout de suite (reprise après veille). Peut être
    /// appelée depuis n'importe quel thread : le prochain <see cref="Tick"/> s'en charge.
    /// </summary>
    public void RequestReconnect()
    {
        Volatile.Write(ref _reconnectRequested, 1);
        _scheduler.Wake();
    }

    /// <summary>
    /// Une itération : abandonne la connexion si on l'a demandé ou si l'écran a disparu (sondé au plus une fois
    /// par seconde), reconnecte si besoin (au plus toutes les 2 s), rend et envoie si l'ordonnanceur le demande,
    /// sinon renvoie la dernière image si rien n'est parti depuis 1 s.
    /// </summary>
    /// <returns>Le délai avant la prochaine itération utile, borné à [0, 1 s] : le réveil par le signal de l'ordonnanceur peut venir avant.</returns>
    public TimeSpan Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.Exchange(ref _reconnectRequested, 0) == 1)
        {
            _screen.MarkLost(ResumeReason);
            _forceReconnect = true;
        }
        else if (_screen.State == ScreenState.Ready && _time.GetElapsedTime(_lastPresenceCheck) >= PresenceInterval)
        {
            _lastPresenceCheck = _time.GetTimestamp();
            if (!_screen.IsPresent()) _screen.MarkLost(ScreenDevice.AbsentReason);
        }

        if (_screen.State != ScreenState.Ready && !TryReconnect())
        {
            return NextDelay();
        }

        AdvanceBackground();
        RenderAndSend();
        KeepAlive();
        return NextDelay();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var module in _moduleList)
        {
            module.Invalidated -= _onInvalidated;
            module.StatusChanged -= _onStatusChanged;
            try
            {
                module.Dispose();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Échec de l'arrêt du module « {module.Descriptor.TypeId} » : {ex}");
            }
        }

        _modules.Clear();

        // Le compositeur garde une copie floutée du fond : on le libère avant le fond.
        _composer.Dispose();
        _background.Dispose();
    }

    /// <summary>
    /// Le temps à dormir d'après l'état après cette itération : jusqu'au prochain essai de connexion si l'écran
    /// n'est pas prêt ; sinon jusqu'à la prochaine image possible (si un redessin est attendu ou si un module
    /// s'anime), au renvoi de l'image ou au sondage de présence. Borné à [0, 1 s].
    /// </summary>
    private TimeSpan NextDelay()
    {
        var delay = MaxDelay;
        if (_screen.State != ScreenState.Ready)
        {
            if (!_forceReconnect && _hasTriedConnect)
            {
                delay = Min(delay, ReconnectInterval - _time.GetElapsedTime(_lastConnectAttempt));
            }
            else
            {
                delay = TimeSpan.Zero;
            }
        }
        else
        {
            delay = Min(delay, PresenceInterval - _time.GetElapsedTime(_lastPresenceCheck));
            if (_screen.LastFrame is not null)
            {
                delay = Min(delay, KeepAliveInterval - _time.GetElapsedTime(_lastSend));
            }

            if (_scheduler.IsDirty || AnyAnimating())
            {
                delay = Min(delay, _scheduler.UntilNextSlot());
            }

            // Un fond animé réveille la boucle à sa prochaine image, jamais plus vite que le plafond d'images par seconde.
            // L'échéance est absolue : le temps passé à rendre et à envoyer l'image ne s'ajoute pas au délai.
            if (_background.IsAnimated)
            {
                var elapsed = _time.GetElapsedTime(_backgroundStart);
                if (_backgroundRetryAt is { } retry)
                {
                    delay = Min(delay, retry - elapsed); // fond en échec : un essai par seconde, pas un réveil par image
                }
                else if (_backgroundDue is { } due)
                {
                    delay = Min(delay, Max(due - elapsed, _scheduler.UntilNextSlot()));
                }
            }
        }

        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>
    /// Avance le fond animé au temps écoulé depuis le démarrage (horloge monotone : un changement d'heure ne bloque pas la
    /// vidéo) et demande une image s'il a changé. Rien tant que l'écran n'est pas prêt : personne ne verrait le résultat.
    /// Note l'échéance absolue de l'image suivante. Si l'avance lève, l'erreur est journalisée (une fois, puis au plus une ligne
    /// de synthèse par minute) et le fond n'est réessayé qu'une fois par seconde : un fond en échec ne réveille pas la boucle
    /// à chaque image.
    /// </summary>
    private void AdvanceBackground()
    {
        if (!_background.IsAnimated || _screen.State != ScreenState.Ready) return;

        var elapsed = _time.GetElapsedTime(_backgroundStart);
        if (_backgroundRetryAt is { } retry && elapsed < retry) return;

        try
        {
            _backgroundRetryAt = null;
            if (_background.Advance(elapsed)) _scheduler.MarkDirty();
            _backgroundDue = _background.UntilNextFrame is { } until ? elapsed + until : null;
        }
        catch (Exception ex)
        {
            _renderErrors.Report($"Échec de l'avance du fond d'écran : {ex}");
            _backgroundDue = null;
            _backgroundRetryAt = elapsed + MaxDelay;
        }
    }

    private bool TryReconnect()
    {
        if (!_forceReconnect && _hasTriedConnect && _time.GetElapsedTime(_lastConnectAttempt) < ReconnectInterval)
        {
            return false;
        }

        _forceReconnect = false;
        _hasTriedConnect = true;
        _lastConnectAttempt = _time.GetTimestamp(); // début de la tentative : une toutes les 2 s exactement
        var connected = _screen.TryConnect();
        if (!connected) return false;

        _lastPresenceCheck = _time.GetTimestamp(); // le sondage de présence repart de la connexion
        _lastSend = _lastPresenceCheck; // TryConnect a renvoyé la dernière image, s'il y en avait une

        // ShouldRender consomme le drapeau : après une reconnexion, on redemande une image.
        _scheduler.MarkDirty();
        return true;
    }

    private void RenderAndSend()
    {
        if (!_scheduler.ShouldRender(AnyAnimating())) return;

        try
        {
            byte[] jpeg;
            var renderStart = _time.GetTimestamp();
            try
            {
                using var bitmap = _composer.Compose(_layout, _modules, _theme, _background, _time.GetLocalNow());
                jpeg = JpegEncoder.Encode(bitmap, _layout.Rotation);
            }
            catch (Exception ex)
            {
                _renderErrors.Report($"Échec du rendu de l'image : {ex}");
                _scheduler.MarkDirty();
                return;
            }

            var sendStart = _time.GetTimestamp();
            var renderTime = _time.GetElapsedTime(renderStart, sendStart);
            bool sent;
            try
            {
                sent = _screen.TrySendFrame(jpeg);
            }
            catch (ArgumentException ex)
            {
                // Au-delà de 450 000 octets le protocole refuse l'image : on l'ignore, sans réessayer en boucle.
                Trace.TraceError($"Image ignorée ({jpeg.Length} octets, trop grande) : {ex.Message}");
                return;
            }

            if (sent)
            {
                _lastSend = _time.GetTimestamp();
                FramesSent++;
                _renderTotal += renderTime;
                _sendTotal += _time.GetElapsedTime(sendStart, _lastSend);
                _timedFrames++;
                if (FramesSent <= 10 || FramesSent % 100 == 0)
                {
                    Trace.TraceInformation($"Image {FramesSent} envoyée ({jpeg.Length} octets){TimingSummary()}");
                }
            }
            else
            {
                _scheduler.MarkDirty(); // l'écran a décroché : l'image sera renvoyée après la reconnexion
            }
        }
        finally
        {
            _scheduler.OnRendered();
        }
    }

    /// <summary>
    /// " ; rendu 41 ms, envoi 58 ms" : les durées moyennes de rendu (composition et encodage) et d'envoi des images envoyées
    /// depuis la ligne de journal précédente ; remet les compteurs à zéro. Sert à mesurer ce que coûte un fond animé.
    /// </summary>
    private string TimingSummary()
    {
        int count = Math.Max(1, _timedFrames);
        var render = (long)Math.Round(_renderTotal.TotalMilliseconds / count);
        var send = (long)Math.Round(_sendTotal.TotalMilliseconds / count);
        var average = _timedFrames > 1 ? " en moyenne" : "";
        _renderTotal = _sendTotal = TimeSpan.Zero;
        _timedFrames = 0;
        return $" ; rendu {render} ms, envoi {send} ms{average}";
    }

    private void KeepAlive()
    {
        if (_screen.State != ScreenState.Ready || _time.GetElapsedTime(_lastSend) < KeepAliveInterval) return;

        var lastFrame = _screen.LastFrame;
        if (lastFrame is null) return;

        _lastSend = _time.GetTimestamp();
        if (_screen.TrySendFrame(lastFrame))
        {
            KeepAlivesSent++;
        }
        else
        {
            _scheduler.MarkDirty(); // l'écran a décroché : une image fraîche partira après la reconnexion
        }
    }

    private bool AnyAnimating()
    {
        foreach (var module in _moduleList)
        {
            if (module.IsAnimating) return true;
        }

        return false;
    }
}
