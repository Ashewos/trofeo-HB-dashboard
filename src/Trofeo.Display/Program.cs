using System.Diagnostics;
using Microsoft.Win32;
using Trofeo.Core;
using Trofeo.Core.Layout;
using Trofeo.Core.Logging;
using Trofeo.Core.Rendering;
using Trofeo.Core.Secrets;
using Trofeo.Core.Shortcuts;
using Trofeo.Device;
using Trofeo.Display.Video;
using Trofeo.Modules.Agents;
using Trofeo.Modules.Clock;
using Trofeo.Modules.Monitoring;
using Trofeo.Modules.Network;
using Trofeo.Modules.NowPlaying;
using Trofeo.Sensors;

namespace Trofeo.Display;

static class Program
{
    private const string MutexName = @"Local\trofeo-HB-dashboard.display";
    private static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromMilliseconds(100);

    [STAThread]
    static int Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) return 0; // une seule instance

        try
        {
            InitializeLog();
            Trace.TraceInformation("Démarrage du service d'affichage.");
            return Run();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Arrêt sur erreur : {ex}");
            return 1;
        }
        finally
        {
            Trace.TraceInformation("Arrêt du service d'affichage.");
            Trace.Flush();
            mutex.ReleaseMutex();
        }
    }

    private static int Run()
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Trace.TraceError($"Exception de l'interface : {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Trace.TraceError($"Exception non gérée : {e.ExceptionObject}");

        var layout = LoadLayout();
        var vault = new DpapiSecretVault(Path.Combine(AppPaths.SettingsDir, "secrets.dat"));
        // Tout ce qui suit est déclaré avant la boucle : libéré après elle, dans l'ordre inverse de la création.
        // Pas de redirection suivie : la session Freebox ne doit jamais partir vers un autre hôte.
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("trofeo-HB-dashboard/0.2");

        // Capteurs : l'assistant élevé s'il tourne, sinon une lecture locale. Aucune panne ici n'arrête le service.
        using var sensorPipe = new SensorPipeClient(SensorPipe.Name, HelperIdentity.IsTrusted, TimeProvider.System);
        StartSensorPipe(sensorPipe);
        using var sensorHub = new SensorHub(() => new LhmSensorReader(), sensorPipe, TimeProvider.System);

        var registry = new ModuleRegistry(
            [
                new ClockFactory(new OpenMeteoClient(http)),
                new MonitoringFactory(sensorHub),
                new NowPlayingFactory(ctx => new SmtcMediaSource(ctx.Log)),
                NetworkFactory.CreateProduction(http),
                ClaudeCodeFactory.CreateProduction(AppLocator.FindDirectory("mods")),
                CodexFactory.CreateProduction(),
            ],
            vault.Scope);
        var screen = new ScreenDevice(new WinUsbTransportFactory(), TimeProvider.System);
        screen.StateChanged += (_, _) =>
        {
            var status = screen.Status;
            Trace.TraceInformation($"Écran : {status.State} ({status.Reason ?? "ok"}).");
        };

        using var scheduler = new FrameScheduler(TimeProvider.System); // déclaré avant la boucle : libéré après elle
        // Apparence lue une fois au démarrage : un changement de réglage demande de relancer le service.
        var theme = AppearanceResolver.Resolve(layout.Appearance ?? AppearanceSettings.Default, new SkiaSystemFonts(), Warn);
        var background = BackgroundFactory.Create(
            layout.Appearance?.Background, theme, path => MediaFoundationBackground.TryOpen(path, Warn), Warn);
        Trace.TraceInformation(
            $"Apparence : thème « {theme.Theme.Id} », fond {(layout.Appearance?.Background?.Kind ?? BackgroundKind.Theme)}"
            + $"{(theme.LegibilityGuard ? "" : ", garde de lisibilité coupée")}.");

        // La boucle devient propriétaire du fond et le libère après elle.
        using var loop = new DisplayLoop(layout, registry, screen, scheduler, TimeProvider.System, theme, background);
        using var cts = new CancellationTokenSource();
        loop.Start(cts.Token);

        // Après une veille, l'écran a pu être réinitialisé sans que le lien le dise : on repart d'une connexion neuve.
        PowerModeChangedEventHandler onPowerChanged = (_, e) =>
        {
            if (e.Mode != PowerModes.Resume) return;
            Trace.TraceInformation("Reprise après veille : reconnexion de l'écran.");
            loop.RequestReconnect();
        };
        SystemEvents.PowerModeChanged += onPowerChanged;

        var thread = new Thread(() => TickLoop(loop, scheduler, cts.Token)) { IsBackground = true, Name = "Trofeo.Display.Tick" };
        thread.Start();

        try
        {
            // Sur le thread de l'interface, avant la pompe de messages. Déclarés après la boucle et dans ce bloc :
            // le routeur (qui retire les raccourcis) puis la fenêtre sont libérés avant elle, plus aucun raccourci
            // ne peut atteindre une boucle libérée.
            using var hotkeys = StartHotkeys(layout, loop);

            using var tray = new TrayIcon(screen);
            Application.Run(tray);
        }
        finally
        {
            SystemEvents.PowerModeChanged -= onPowerChanged; // sinon le gestionnaire fuit
            cts.Cancel();
            if (!thread.Join(TimeSpan.FromSeconds(15)))
            {
                Trace.TraceError("La boucle d'affichage ne s'est pas arrêtée à temps.");
                Trace.Flush();
                Environment.Exit(1); // le thread est de fond : on évite de libérer ce qu'il utilise encore
            }

            screen.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// Crée la fenêtre des raccourcis et enregistre ceux de la disposition. Une panne ici n'arrête pas le service :
    /// il tourne sans raccourcis (<c>null</c>), de même quand aucun module n'accepte les commandes. Libérer le résultat
    /// retire les raccourcis puis détruit la fenêtre.
    /// </summary>
    private static IDisposable? StartHotkeys(DashboardLayout layout, DisplayLoop loop)
    {
        // Les modules ne changent pas en cours de route : sans module commandable au départ, un raccourci ne servirait
        // à rien et prendrait une combinaison à d'autres applications.
        if (loop.FindCommandTarget(null) is null)
        {
            Trace.TraceInformation("Aucun module de la disposition n'accepte les commandes : pas de raccourcis clavier.");
            return null;
        }

        HotkeyWindow? window = null;
        HotkeyRouter? router = null;
        try
        {
            window = new HotkeyWindow();
            router = new HotkeyRouter(window, layout.Shortcuts ?? ShortcutDefaults.All, loop.FindCommandTarget);
            window.Hotkey += router.OnHotkey;
            router.RegisterAll();
            return new HotkeyScope(router, window);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Raccourcis clavier indisponibles, le service tourne sans : {ex.Message}");
            router?.Dispose();
            window?.Dispose();
            return null;
        }
    }

    /// <summary>Libère le routeur (qui retire les raccourcis) avant la fenêtre qui les portait.</summary>
    private sealed class HotkeyScope(HotkeyRouter router, HotkeyWindow window) : IDisposable
    {
        public void Dispose()
        {
            router.Dispose();
            window.Dispose();
        }
    }

    /// <summary>Lance la boucle de connexion au tube en tâche de fond ; <see cref="SensorPipeClient.Dispose"/> l'arrête.</summary>
    private static void StartSensorPipe(SensorPipeClient client) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await client.RunAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Liaison avec l'assistant des capteurs interrompue : {ex}");
            }
        });

    private static void TickLoop(DisplayLoop loop, FrameScheduler scheduler, CancellationToken ct)
    {
        WaitHandle[] handles = [ct.WaitHandle, scheduler.Signal];
        var errors = new RateLimitedErrorLog(TimeProvider.System); // une panne qui se répète à chaque tour ne remplit pas le journal
        while (!ct.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = loop.Tick();
            }
            catch (Exception ex)
            {
                errors.Report($"Erreur dans la boucle d'affichage : {ex}");
                ct.WaitHandle.WaitOne(ErrorRetryDelay); // pas de boucle serrée si l'erreur se répète
                continue;
            }

            WaitHandle.WaitAny(handles, delay); // dort jusqu'à l'échéance, un signal ou l'arrêt
        }
    }

    // Trace.TraceWarning est conditionnelle (TRACE) : elle ne peut pas servir de délégué, d'où ce relais.
    private static void Warn(string message) => Trace.TraceWarning(message);

    private static DashboardLayout LoadLayout()
    {
        var path = Path.Combine(AppPaths.SettingsDir, "layout.json");
        var store = new LayoutStore(path, Warn);
        if (!File.Exists(path))
        {
            try
            {
                store.Save(DashboardLayout.Default);
                Trace.TraceInformation($"Disposition par défaut enregistrée : {path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Impossible d'enregistrer la disposition par défaut : {ex.Message}");
            }
        }

        var layout = store.Load();
        Trace.TraceInformation($"Disposition chargée : {layout.Placements.Count} module(s), rotation {layout.Rotation}°.");
        return layout;
    }

    /// <summary>Le journal est un confort : s'il ne peut pas s'ouvrir, le service tourne sans.</summary>
    private static void InitializeLog()
    {
        try
        {
            OpenLog();
        }
        catch (Exception)
        {
            // Dossier inaccessible, disque plein, droits : pas de journal, mais on démarre.
        }
    }

    private static void OpenLog()
    {
        var dir = Path.Combine(AppPaths.SettingsDir, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "display.log");
        Trace.Listeners.Add(new FileTraceListener(path));
        Trace.AutoFlush = true;
    }
}
