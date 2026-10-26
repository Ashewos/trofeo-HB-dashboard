using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.Control;
using Trofeo.Settings.Services;

namespace Trofeo.Settings.ViewModels;

/// <summary>
/// La fenêtre : l'en-tête (onglets, pastille de l'écran), la bande en direct et le contenu de l'onglet choisi. Quand le
/// service est arrêté, un panneau remplace le contenu des onglets ; l'onglet choisi, lui, n'est pas perdu.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IServiceLink _link;

    public MainViewModel(IServiceLink link, LayoutDocument document, EditorPreferences preferences)
    {
        _link = link;
        Document = document;
        Preferences = preferences;
        Tabs =
        [
            new LayoutTabViewModel(), new AppearanceTabViewModel(), new ModulesTabViewModel(),
            new ShortcutsTabViewModel(), new SettingsTabViewModel(),
        ];
        _selectedTab = Tabs[0];

        _link.SetRealTimePreview(preferences.RealTimePreview);
        _link.Changed += Refresh;
        _link.FrameReceived += ShowFrame;
        Refresh();
    }

    public IServiceLink Link => _link;
    public LayoutDocument Document { get; }
    public EditorPreferences Preferences { get; }
    public IReadOnlyList<TabViewModel> Tabs { get; }

    [ObservableProperty]
    private TabViewModel _selectedTab;

    /// <summary>Le service ne tourne pas : le panneau « Démarrer le service » remplace les onglets.</summary>
    [ObservableProperty]
    private bool _isStopped;

    /// <summary>Le démarrage a échoué : Trofeo.Display.exe est introuvable.</summary>
    [ObservableProperty]
    private bool _startFailed;

    [ObservableProperty]
    private string _pillText = "";

    // Le point de la pastille : un seul des quatre est vrai.
    [ObservableProperty] private bool _pillOnline;
    [ObservableProperty] private bool _pillIdle;
    [ObservableProperty] private bool _pillAmber;
    [ObservableProperty] private bool _pillRefuse;

    /// <summary>La dernière image de l'écran, décodée ; <c>null</c> tant qu'aucune n'est arrivée.</summary>
    [ObservableProperty]
    private Bitmap? _previewImage;

    /// <summary>Rotation que l'écran applique à cette image, en degrés.</summary>
    [ObservableProperty]
    private int _previewRotation;

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        StartFailed = false;
        StartFailed = !await _link.StartServiceAsync();
    }

    private void Refresh()
    {
        IsStopped = _link.State == LinkState.ServiceStopped;
        if (!IsStopped)
            StartFailed = false;

        var (text, dot) = Pill();
        PillText = text;
        PillOnline = dot == Dot.Online;
        PillIdle = dot == Dot.Idle;
        PillAmber = dot == Dot.Amber;
        PillRefuse = dot == Dot.Refuse;
    }

    private enum Dot { Online, Idle, Amber, Refuse }

    private (string Text, Dot Dot) Pill()
    {
        if (_link.State == LinkState.ServiceStopped)
            return (Texts.LinkStopped, Dot.Refuse);
        if (_link.State != LinkState.Connected || _link.Status is not { } status)
            return (Texts.LinkConnecting, Dot.Idle);
        if (status.Emulated)
            return (Texts.EmulatedMode, Dot.Amber);
        return status.Screen.State switch
        {
            LinkScreenState.Ready => (Texts.ScreenOnline, Dot.Online),
            LinkScreenState.Recovering => (Texts.ScreenRecovering, Dot.Amber),
            _ => (Texts.ScreenIdle, Dot.Idle),
        };
    }

    private void ShowFrame(PreviewFrame frame)
    {
        Bitmap decoded;
        try
        {
            using var stream = new MemoryStream(frame.Jpeg, writable: false);
            decoded = new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return; // Une image abîmée est ignorée : la suivante arrive dans un quart de seconde.
        }

        var previous = PreviewImage;
        PreviewRotation = frame.Rotation;
        PreviewImage = decoded;
        previous?.Dispose();
    }

    public void Dispose()
    {
        _link.Changed -= Refresh;
        _link.FrameReceived -= ShowFrame;
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
