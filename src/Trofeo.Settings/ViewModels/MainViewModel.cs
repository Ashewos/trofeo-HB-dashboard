using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.Control;
using Trofeo.Core.Rendering;
using Trofeo.Settings.Services;

namespace Trofeo.Settings.ViewModels;

/// <summary>
/// La fenêtre : l'en-tête (onglets, pastille de l'écran), la bande en direct et le contenu de l'onglet choisi. Quand le
/// service est arrêté, un panneau remplace le contenu des onglets ; l'onglet choisi, lui, n'est pas perdu.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IServiceLink _link;

    public MainViewModel(
        IServiceLink link, LayoutDocument document, EditorPreferences preferences,
        IFilePicker? picker = null, IReadOnlyList<string>? systemFonts = null, ISecretAccess? secrets = null, TimeProvider? time = null,
        string? preferencesPath = null, SystemServices? system = null)
    {
        _link = link;
        Session = new EditSession(document);
        LayoutTab = new LayoutTabViewModel(link, Session);
        AppearanceTab = new AppearanceTabViewModel(Session, picker, systemFonts ?? new SkiaSystemFonts().Families);
        ModulesTab = new ModulesTabViewModel(link, Session, secrets ?? new NoSecretAccess(), time);
        ShortcutsTab = new ShortcutsTabViewModel(link, Session);
        PreferencesStore = new PreferencesStore(preferences, preferencesPath);
        SettingsTab = new SettingsTabViewModel(
            link, Session, PreferencesStore, system ?? SystemServices.None, picker);
        Document = document;
        Tabs = [LayoutTab, AppearanceTab, ModulesTab, ShortcutsTab, SettingsTab];
        _selectedTab = Tabs[0];

        _link.SetRealTimePreview(preferences.RealTimePreview);
        _link.Changed += Refresh;
        _link.FrameReceived += ShowFrame;
        Session.Changed += RefreshBanner;
        Refresh();
        RefreshBanner();
    }

    public IServiceLink Link => _link;

    /// <summary>Les modifications de la disposition et leur historique, communs à tous les onglets (Ctrl+Z, Ctrl+Y).</summary>
    public EditSession Session { get; }

    /// <summary>L'onglet Disposition : le damier de la fenêtre travaille avec lui.</summary>
    public LayoutTabViewModel LayoutTab { get; }

    public AppearanceTabViewModel AppearanceTab { get; }
    public ModulesTabViewModel ModulesTab { get; }
    public ShortcutsTabViewModel ShortcutsTab { get; }
    public SettingsTabViewModel SettingsTab { get; }
    public LayoutDocument Document { get; }

    /// <summary>Les préférences de la fenêtre (aperçu en rendu réel, première ouverture), écrites dans <c>settings-app.json</c> quand elles changent.</summary>
    public PreferencesStore PreferencesStore { get; }

    public EditorPreferences Preferences => PreferencesStore.Current;
    public IReadOnlyList<TabViewModel> Tabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridVisible))]
    private TabViewModel _selectedTab;

    // Un texte tapé dans l'onglet Modules et pas encore validé s'écrit quand on le quitte ; une capture de raccourci s'abandonne.
    partial void OnSelectedTabChanging(TabViewModel? oldValue, TabViewModel newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
            return;
        if (oldValue is ModulesTabViewModel modules)
            _ = modules.CommitPendingAsync();
        else if (oldValue is ShortcutsTabViewModel shortcuts)
            shortcuts.CancelCapture();
    }

    partial void OnSelectedTabChanged(TabViewModel value) => RefreshBanner();

    /// <summary>
    /// La ligne <c>Amber</c> sous la bande, dans tous les onglets : le message de la dernière modification (refus du
    /// service, écriture impossible) puis les avertissements du rechargement qui ne regardent pas l'apparence. Les autres
    /// avertissements sont sous le réglage concerné, dans l'onglet Personnalisation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveMessage))]
    private string? _saveMessage;

    public bool HasSaveMessage => !string.IsNullOrEmpty(SaveMessage);

    /// <summary>Le service ne tourne pas : le panneau « Démarrer le service » remplace les onglets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridVisible))]
    private bool _isStopped;

    /// <summary>Le damier se pose sur la bande dans l'onglet Disposition, tant que le service tourne.</summary>
    public bool IsGridVisible => SelectedTab is LayoutTabViewModel && !IsStopped;

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

    /// <summary>Fenêtre réduite : plus d'images à décoder ni à demander au service ; reprise à la restauration.</summary>
    public void SetPreviewPaused(bool paused) => _link.SetPreviewPaused(paused);

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        StartFailed = false;
        StartFailed = !await _link.StartServiceAsync();
    }

    private void RefreshBanner()
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(Session.Message))
            lines.Add(Session.Message);
        // Dans l'onglet Raccourcis, ceux que Windows a refusés sont déjà sur leur ligne.
        var onShortcuts = SelectedTab is ShortcutsTabViewModel;
        lines.AddRange(Session.OtherWarnings.Where(w => !(onShortcuts && ShortcutsTabViewModel.IsHotkeyFailure(w))));
        SaveMessage = lines.Count == 0 ? null : string.Join('\n', lines);
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
        Session.Changed -= RefreshBanner;
        LayoutTab.Dispose();
        AppearanceTab.Dispose();
        ModulesTab.Dispose();
        ShortcutsTab.Dispose();
        SettingsTab.Dispose();
        Session.Dispose();
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
