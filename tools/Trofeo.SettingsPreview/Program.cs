using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using SkiaSharp;
using Trofeo.Control;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Settings;
using Trofeo.Settings.Services;
using Trofeo.Settings.Tests;
using Trofeo.Settings.ViewModels;
using Trofeo.Settings.Views;
using Trofeo.ThemePreview;

namespace Trofeo.SettingsPreview;

/// <summary>
/// Images PNG de la fenêtre de réglage : une par onglet, plus « service arrêté » et « mode émulé ». La fenêtre est la
/// vraie, rendue sans affichage (Avalonia.Headless + Skia) ; le service est un faux <see cref="IServiceLink"/>, et la
/// bande montre le vrai tableau de bord (les vrais modules de ThemePreview), tourné de 180° comme le service l'envoie.
/// <c>--out &lt;dossier&gt;</c> (par défaut : apercus-reglages).
/// </summary>
internal static class Program
{
    private const string Usage = "Usage : Trofeo.SettingsPreview [--out <dossier>]   (par défaut : apercus-reglages)";

    private static async Task<int> Main(string[] args)
    {
        var folder = "apercus-reglages";
        if (args.Length == 2 && args[0] == "--out")
            folder = args[1];
        else if (args.Length != 0)
        {
            Console.Error.WriteLine(Usage);
            return args is ["--help"] or ["-h"] ? 0 : 2;
        }

        Directory.CreateDirectory(folder);
        var jpeg = await ComposeScreenJpegAsync().ConfigureAwait(false);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        // Les captures se font sur le fil de l'interface (celui-ci).
        var scratch = Path.Combine(Path.GetTempPath(), "trofeo-settings-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            RenderAll(folder, scratch, jpeg);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        return 0;
    }

    private static void RenderAll(string folder, string scratch, byte[] jpeg)
    {
        var connected = (Welcome: SampleData.Welcome(), Status: SampleData.Status());

        // Un onglet par image, sur le service branché.
        var tabs = new[] { "disposition", "personnalisation", "modules", "raccourcis", "parametres" };
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            Render(folder, scratch, tabs[i] + ".png", 1440, 900, jpeg,
                link => link.Set(LinkState.Connected, connected.Welcome, connected.Status),
                (_, model) => model.SelectedTab = model.Tabs[index]);
        }

        Render(folder, scratch, "disposition-1200x760.png", 1200, 760, jpeg,
            link => link.Set(LinkState.Connected, connected.Welcome, connected.Status), (_, _) => { });

        // L'onglet Disposition : un module choisi, des glissements (libre, refusé), l'ajout, l'oubli à confirmer.
        var layoutStatus = connected.Status with
        {
            Modules =
            [
                new ModuleState(ClaudeId, "claudecode", Trofeo.ModuleKit.ModuleHealth.Online, null, []),
                new ModuleState(MonitoringId, "monitoring", Trofeo.ModuleKit.ModuleHealth.Online, null, []),
                new ModuleState(NowPlayingId, "nowplaying", Trofeo.ModuleKit.ModuleHealth.NotConfigured, null, []),
                new ModuleState(ClockId, "clock", Trofeo.ModuleKit.ModuleHealth.Online, null, []),
                new ModuleState(NetworkId, "network", Trofeo.ModuleKit.ModuleHealth.Offline, null, []),
            ],
        };
        void Connected(FakeServiceLink link) => link.Set(LinkState.Connected, connected.Welcome, layoutStatus);

        Render(folder, scratch, "disposition-module-choisi.png", 1440, 900, jpeg, Connected,
            (_, model) => model.LayoutTab.Select(MonitoringId));

        Render(folder, scratch, "disposition-module-choisi-1200x760.png", 1200, 760, jpeg, Connected,
            (_, model) => model.LayoutTab.Select(ClaudeId));

        Render(folder, scratch, "disposition-glissement-refuse.png", 1440, 900, jpeg, Connected, (_, model) =>
        {
            model.LayoutTab.BeginMove(ClockId, CellCenter(12, 0));
            model.LayoutTab.UpdateDrag(CellCenter(9, 0));   // sur Monitoring PC
        });

        Render(folder, scratch, "disposition-glissement-libre.png", 1440, 900, jpeg, Connected, (_, model) =>
        {
            model.LayoutTab.BeginResize(ClaudeId, ResizeEdge.Right);
            model.LayoutTab.UpdateDrag(new Avalonia.Point(GridGeometry.ModuleRect(6, 0, 1, 1).Right + GridGeometry.Gap / 2.0, 200));   // Claude Code rétréci à 7 colonnes : libre
        });

        Render(folder, scratch, "disposition-glissement-trop-petit.png", 1440, 900, jpeg, Connected, (_, model) =>
        {
            model.LayoutTab.BeginResize(ClaudeId, ResizeEdge.Right);
            model.LayoutTab.UpdateDrag(CellCenter(3, 2));   // plus étroit que le minimum de Claude Code
        });

        Render(folder, scratch, "disposition-ajout.png", 1440, 900, jpeg, Connected, (window, _) =>
        {
            var add = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddButton");
            add.Flyout!.ShowAt(add);
        });

        Render(folder, scratch, "disposition-oublier.png", 1440, 900, jpeg, Connected,
            (_, model) => model.LayoutTab.AskForget(model.LayoutTab.Shelved[0]));

        Render(folder, scratch, "disposition-message-du-service.png", 1440, 900, jpeg, Connected, (_, model) =>
        {
            model.LayoutTab.Select(ClockId);
            model.LayoutTab.SetTextSizeAsync(Trofeo.ModuleKit.TextSize.Large).GetAwaiter().GetResult();
        }, refuseReload: "Le service n'a pas pu appliquer la disposition : le fond choisi est introuvable.");

        Render(folder, scratch, "disposition-pixel.png", 1440, 900, jpeg, Connected,
            (_, model) => model.LayoutTab.Select(ClockId),
            layout: Base with { Appearance = new AppearanceSettings("pixel") });

        Render(folder, scratch, "mode-emule.png", 1440, 900, jpeg,
            link => link.Set(LinkState.Connected, connected.Welcome, SampleData.Status(LinkScreenState.Disconnected, emulated: true)),
            (_, _) => { });

        Render(folder, scratch, "ecran-non-branche.png", 1440, 900, jpeg,
            link => link.Set(LinkState.Connected, connected.Welcome, SampleData.Status(LinkScreenState.Disconnected)),
            (_, _) => { });

        Render(folder, scratch, "reprise-de-l-ecran.png", 1440, 900, jpeg,
            link => link.Set(LinkState.Connected, connected.Welcome, SampleData.Status(LinkScreenState.Recovering)),
            (_, _) => { });

        // Le focus au clavier : contour de 2 px en Accent (sur un onglet, puis sur le bouton principal).
        Render(folder, scratch, "focus-onglet.png", 1440, 900, jpeg,
            link => link.Set(LinkState.Connected, connected.Welcome, connected.Status),
            (window, _) => FocusByKeyboard(window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("tabs")).ContainerFromIndex(1)!));

        Render(folder, scratch, "focus-bouton.png", 1440, 900, null,
            link => link.Set(LinkState.ServiceStopped),
            (window, _) => FocusByKeyboard(window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible)));

        Render(folder, scratch, "service-arrete.png", 1440, 900, null,
            link => link.Set(LinkState.ServiceStopped), (_, _) => { });

        Render(folder, scratch, "service-introuvable.png", 1440, 900, null,
            link => { link.StartResult = false; link.Set(LinkState.ServiceStopped); },
            (_, model) => model.StartServiceCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        Render(folder, scratch, "connexion.png", 1440, 900, null,
            link => link.Set(LinkState.Connecting), (_, _) => { });
    }

    private static readonly Guid ClaudeId = new("3c1d9f5e-7a2b-4e8c-9b1f-6d2a0e4c8b17");
    private static readonly Guid MonitoringId = new("246ac06d-eec2-4d64-9930-24b64603a03b");
    private static readonly Guid NowPlayingId = new("8ebcc647-ab4b-401d-b2b3-f7a0ebd97b4b");
    private static readonly Guid ClockId = new("5b0c5e2e-6a53-4c0b-9d0e-3f1a7c1d2b01");
    private static readonly Guid NetworkId = new("a2002423-c807-4403-9c5a-2a983fb6a371");

    /// <summary>La disposition par défaut (celle de la bande), avec Codex rangé parmi les modules retirés.</summary>
    private static readonly DashboardLayout Base = DashboardLayout.Default with
    {
        Shelf =
        [
            new ModulePlacement(new Guid("c0de0000-0000-4000-8000-000000000001"), "codex", 0, 0, 8, 4,
                Trofeo.ModuleKit.TextSize.Normal, System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()),
        ],
    };

    /// <summary>Le centre d'une case, en pixels de l'écran.</summary>
    private static Avalonia.Point CellCenter(int column, int row)
    {
        var rect = GridGeometry.ModuleRect(column, row, 1, 1);
        return new Avalonia.Point((rect.Left + rect.Right) / 2.0, (rect.Top + rect.Bottom) / 2.0);
    }

    private static void FocusByKeyboard(Avalonia.Controls.Control control) => control.Focus(NavigationMethod.Tab);

    private static void Render(
        string folder, string scratch, string name, int width, int height, byte[]? jpeg,
        Action<FakeServiceLink> state, Action<MainWindow, MainViewModel> arrange,
        DashboardLayout? layout = null, string? refuseReload = null)
    {
        var link = new FakeServiceLink();
        state(link);
        if (refuseReload is not null)
            link.OnReload = () => Task.FromResult(new Reloaded(false, refuseReload, []));
        var store = new LayoutStore(Path.Combine(scratch, "layout.json"));
        store.Save(layout ?? Base);
        var document = new LayoutDocument(store, link);
        document.Load();
        using var model = new MainViewModel(link, document, new EditorPreferences());
        var window = new MainWindow { DataContext = model, Width = width, Height = height };
        window.Show();
        if (jpeg is not null)
            link.Push(new PreviewFrame(jpeg, 180));
        arrange(window, model);

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var file = Path.Combine(folder, name);
        window.CaptureRenderedFrame()!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Console.WriteLine("Aperçu écrit : " + file);
        window.Close();
    }

    /// <summary>Le tableau de bord réel (thème Classique), tourné de 180° et encodé en JPEG : l'image que le service envoie.</summary>
    private static async Task<byte[]> ComposeScreenJpegAsync()
    {
        using var scene = await PreviewScene.CreateAsync(longTexts: false).ConfigureAwait(false);
        using var composer = new FrameComposer();
        var theme = AppearanceResolver.Resolve(new AppearanceSettings("classique"), new SkiaSystemFonts(), Warn);
        using var background = BackgroundFactory.Create(null, theme, null, Warn);
        using var frame = composer.Compose(PreviewScene.Layout(Trofeo.ModuleKit.TextSize.Normal), scene.ByPlacement, theme, background, PreviewScene.Now);

        using var rotated = new SKBitmap(frame.Width, frame.Height);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Scale(-1, -1, frame.Width / 2f, frame.Height / 2f);
            canvas.DrawBitmap(frame, 0, 0);
        }

        using var image = SKImage.FromBitmap(rotated);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    private static void Warn(string message) => Console.Error.WriteLine("Avertissement : " + message);
}
