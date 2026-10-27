using Avalonia;
using Avalonia.Headless;
using Trofeo.Settings;
using Trofeo.Settings.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// Les tests de la fenêtre partagent un seul fil d'interface (celui de la session sans affichage) ; lancés en même temps que
// d'autres classes, certains démarraient parfois ailleurs que sur ce fil (« un autre fil possède cet objet »). Un par un.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Trofeo.Settings.Tests;

/// <summary>L'application des tests : la vraie <see cref="App"/>, avec un rendu Skia sans affichage.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
