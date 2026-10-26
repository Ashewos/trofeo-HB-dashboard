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

        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
