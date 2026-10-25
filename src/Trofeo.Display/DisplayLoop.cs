using System.Diagnostics;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Device;
using Trofeo.ModuleKit;

namespace Trofeo.Display;

/// <summary>
/// La boucle d'affichage : reconnecte l'écran quand il est absent, puis rend et envoie une image
/// quand un module en a demandé une. Possède les modules qu'elle crée ; <see cref="Tick"/> s'appelle
/// depuis un seul thread.
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

    public DisplayLoop(
        DashboardLayout layout, ModuleRegistry registry, ScreenDevice screen, FrameScheduler scheduler, TimeProvider time)
    {
        _layout = layout;
        _screen = screen;
        _scheduler = scheduler;
        _time = time;
        _onInvalidated = (_, _) => _scheduler.MarkDirty();
        _onStatusChanged = OnStatusChanged;
        _renderErrors = new RateLimitedErrorLog(time);
        _composer = new FrameComposer(_renderErrors);

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
        }

        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

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
            try
            {
                using var bitmap = _composer.Compose(_layout, _modules, Themes.Classique, _time.GetLocalNow());
                jpeg = JpegEncoder.Encode(bitmap, _layout.Rotation);
            }
            catch (Exception ex)
            {
                _renderErrors.Report($"Échec du rendu de l'image : {ex}");
                _scheduler.MarkDirty();
                return;
            }

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
                if (FramesSent <= 10 || FramesSent % 100 == 0)
                {
                    Trace.TraceInformation($"Image {FramesSent} envoyée ({jpeg.Length} octets).");
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
