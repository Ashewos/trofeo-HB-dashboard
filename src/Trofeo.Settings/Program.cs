using System.Diagnostics;
using Avalonia;
using Trofeo.Settings.Services;

namespace Trofeo.Settings;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Une seule fenêtre : un second lancement ramène la première au premier plan, puis quitte.
        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
            return 0;

        // Le journal et le filet d'abord : un plantage de la fenêtre laisse une trace dans logs\settings.log.
        var log = SettingsLog.TryOpen(SettingsLog.DefaultPath);
        using var guard = new CrashGuard();
        App.Guard = guard;
        guard.InstallProcessHandlers();
        Trace.TraceInformation("Démarrage de la fenêtre de réglage.");

        try
        {
            App.Instance = instance;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Trace.TraceInformation("Arrêt de la fenêtre de réglage.");
            Trace.Flush();
            if (log is not null)
                Trace.Listeners.Remove(log);
        }
    }

    // Rendu logiciel : avec la carte graphique (Direct3D par ANGLE), le pilote réserve environ 150 Mo dans le processus
    // (189 à 215 Mo de mémoire propre contre 48 à 52 Mo, mesurés sur le PC d'Alex), pour 0,05 point de processeur gagné.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
}
