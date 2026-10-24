using System.Diagnostics;
using Microsoft.Win32;
using Trofeo.Core;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Device;
using Trofeo.Modules.Clock;

namespace Trofeo.Display;

static class Program
{
    private const string MutexName = @"Local\trofeo-HB-dashboard.display";
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(10);

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
        var registry = new ModuleRegistry([new ClockFactory()]);
        var screen = new ScreenDevice(new WinUsbTransportFactory(), TimeProvider.System);
        screen.StateChanged += (_, _) => Trace.TraceInformation($"Écran : {screen.State} ({screen.Reason ?? "ok"}).");

        using var loop = new DisplayLoop(layout, registry, screen, new FrameScheduler(TimeProvider.System), TimeProvider.System);
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

        var thread = new Thread(() => TickLoop(loop, cts.Token)) { IsBackground = true, Name = "Trofeo.Display.Tick" };
        thread.Start();

        try
        {
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

    private static void TickLoop(DisplayLoop loop, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                loop.Tick();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Erreur dans la boucle d'affichage : {ex}");
            }

            ct.WaitHandle.WaitOne(TickInterval);
        }
    }

    private static DashboardLayout LoadLayout()
    {
        var path = Path.Combine(AppPaths.SettingsDir, "layout.json");
        var store = new LayoutStore(path);
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
