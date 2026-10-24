using System.Diagnostics;
using System.Text;
using Trofeo.Core;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Device;
using Trofeo.Modules.Clock;

namespace Trofeo.Display;

static class Program
{
    private const string MutexName = @"Local\trofeo-HB-dashboard.display";
    private const long MaxLogBytes = 1_000_000;
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

        var thread = new Thread(() => TickLoop(loop, cts.Token)) { IsBackground = true, Name = "Trofeo.Display.Tick" };
        thread.Start();

        try
        {
            using var tray = new TrayIcon(screen);
            Application.Run(tray);
        }
        finally
        {
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

    private static void InitializeLog()
    {
        var dir = Path.Combine(AppPaths.SettingsDir, "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "display.log");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
            {
                File.Move(path, path + ".1", overwrite: true);
            }
        }
        catch (IOException)
        {
            // Journal verrouillé : on continue d'écrire dans le fichier courant.
        }

        Trace.Listeners.Add(new FileTraceListener(path));
        Trace.AutoFlush = true;
    }
}

/// <summary>Écrit chaque message du <see cref="Trace"/> sur une ligne horodatée, dans un fichier lisible pendant l'exécution.</summary>
internal sealed class FileTraceListener : TraceListener
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public FileTraceListener(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message) => WriteEntry("Info", message);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        WriteEntry(Level(eventType), message);

    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
        WriteEntry(Level(eventType), args is { Length: > 0 } && format is not null ? string.Format(format, args) : format);

    private static string Level(TraceEventType type) => type switch
    {
        TraceEventType.Critical or TraceEventType.Error => "Erreur",
        TraceEventType.Warning => "Avert",
        _ => "Info",
    };

    private void WriteEntry(string level, string? message)
    {
        lock (_gate)
        {
            try
            {
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Le journal ne doit jamais faire tomber le service.
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _writer.Dispose();
        base.Dispose(disposing);
    }
}
