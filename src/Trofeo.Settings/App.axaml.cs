using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Trofeo.Core;
using Trofeo.Core.Layout;
using Trofeo.Settings.Services;
using Trofeo.Settings.ViewModels;
using Trofeo.Settings.Views;

namespace Trofeo.Settings;

public partial class App : Application
{
    /// <summary>Le verrou « une seule fenêtre » ; posé par <see cref="Program"/> avant le démarrage de l'interface.</summary>
    internal static SingleInstance? Instance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Sans durée de vie de bureau (tests sans affichage, aperçus), l'appelant monte sa propre fenêtre.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var link = new ServiceLink();
            var store = new LayoutStore(Path.Combine(AppPaths.SettingsDir, "layout.json"));
            var document = new LayoutDocument(store, link);
            document.Load();
            var preferences = EditorPreferences.Load(Path.Combine(AppPaths.SettingsDir, "settings-app.json"));

            var model = new MainViewModel(link, document, preferences);
            var window = new MainWindow { DataContext = model };
            desktop.MainWindow = window;

            if (Instance is { } instance)
                instance.ShowRequested += () => Dispatcher.UIThread.Post(window.BringToFront);

            desktop.Exit += (_, _) =>
            {
                model.Dispose();
                link.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
