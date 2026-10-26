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
            (window, _) => FocusByKeyboard(window.GetVisualDescendants().OfType<ListBox>().Single().ContainerFromIndex(1)!));

        Render(folder, scratch, "focus-bouton.png", 1440, 900, null,
            link => link.Set(LinkState.ServiceStopped),
            (window, _) => FocusByKeyboard(window.GetVisualDescendants().OfType<Button>().Single()));

        Render(folder, scratch, "service-arrete.png", 1440, 900, null,
            link => link.Set(LinkState.ServiceStopped), (_, _) => { });

        Render(folder, scratch, "service-introuvable.png", 1440, 900, null,
            link => { link.StartResult = false; link.Set(LinkState.ServiceStopped); },
            (_, model) => model.StartServiceCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        Render(folder, scratch, "connexion.png", 1440, 900, null,
            link => link.Set(LinkState.Connecting), (_, _) => { });
    }

    private static void FocusByKeyboard(Avalonia.Controls.Control control) => control.Focus(NavigationMethod.Tab);

    private static void Render(
        string folder, string scratch, string name, int width, int height, byte[]? jpeg,
        Action<FakeServiceLink> state, Action<MainWindow, MainViewModel> arrange)
    {
        var link = new FakeServiceLink();
        state(link);
        var document = new LayoutDocument(new LayoutStore(Path.Combine(scratch, "layout.json")), link);
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
