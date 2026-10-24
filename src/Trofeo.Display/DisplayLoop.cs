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
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);

    private readonly DashboardLayout _layout;
    private readonly ScreenDevice _screen;
    private readonly FrameScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly FrameComposer _composer = new();
    private readonly Dictionary<Guid, IModule> _modules = [];
    private readonly IModule[] _moduleList;
    private readonly EventHandler _onInvalidated;
    private bool _hasTriedConnect;
    private long _lastConnectAttempt;
    private bool _disposed;

    public DisplayLoop(
        DashboardLayout layout, ModuleRegistry registry, ScreenDevice screen, FrameScheduler scheduler, TimeProvider time)
    {
        _layout = layout;
        _screen = screen;
        _scheduler = scheduler;
        _time = time;
        _onInvalidated = (_, _) => _scheduler.MarkDirty();

        foreach (var placement in layout.Placements)
        {
            var error = LayoutValidator.Check(layout, placement, registry.Descriptors);
            if (error != PlacementError.None)
            {
                Trace.TraceWarning($"Module ignoré : « {placement.TypeId} » ({placement.InstanceId}) refusé : {error}.");
                continue;
            }

            var module = registry.Create(placement, time);
            if (module is null) continue;

            module.Invalidated += _onInvalidated;
            _modules[placement.InstanceId] = module;
        }

        _moduleList = [.. _modules.Values];
        _scheduler.MarkDirty(); // la première image part dès que l'écran est prêt
    }

    /// <summary>Nombre d'images envoyées et validées par l'écran depuis le démarrage.</summary>
    public int FramesSent { get; private set; }

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

    /// <summary>Une itération : reconnecte si besoin (au plus toutes les 2 s), rend et envoie si l'ordonnanceur le demande.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_screen.State != ScreenState.Ready && !TryReconnect())
        {
            return;
        }

        RenderAndSend();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var module in _moduleList)
        {
            module.Invalidated -= _onInvalidated;
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

    private bool TryReconnect()
    {
        if (_hasTriedConnect && _time.GetElapsedTime(_lastConnectAttempt) < ReconnectInterval)
        {
            return false;
        }

        _hasTriedConnect = true;
        _lastConnectAttempt = _time.GetTimestamp(); // début de la tentative : une toutes les 2 s exactement
        var connected = _screen.TryConnect();
        if (!connected) return false;

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
                Trace.TraceError($"Échec du rendu de l'image : {ex}");
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

    private bool AnyAnimating()
    {
        foreach (var module in _moduleList)
        {
            if (module.IsAnimating) return true;
        }

        return false;
    }
}
