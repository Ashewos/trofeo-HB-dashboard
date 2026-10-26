using Avalonia;
using Avalonia.Headless;
using Trofeo.Settings;
using Trofeo.Settings.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Trofeo.Settings.Tests;

/// <summary>L'application des tests : la vraie <see cref="App"/>, avec un rendu Skia sans affichage.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
