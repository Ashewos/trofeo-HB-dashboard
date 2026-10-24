using Trofeo.Device;

namespace Trofeo.Display;

/// <summary>Icône de la zone de notification : l'état de l'écran en info-bulle et « Quitter » dans le menu.</summary>
public sealed class TrayIcon : ApplicationContext
{
    private readonly ScreenDevice _screen;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly SynchronizationContext? _ui;
    private bool _disposed;

    /// <summary>À créer sur le thread de l'interface, juste avant <c>Application.Run</c>.</summary>
    public TrayIcon(ScreenDevice screen)
    {
        _screen = screen;

        _menu = new ContextMenuStrip(); // un contrôle : installe le contexte de synchronisation WinForms
        _menu.Items.Add("Quitter", null, (_, _) => ExitThread());
        _ui = SynchronizationContext.Current;

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = Tooltip(screen.State),
            ContextMenuStrip = _menu,
            Visible = true,
        };

        screen.StateChanged += OnStateChanged;
    }

    /// <summary>Texte de l'info-bulle pour un état de l'écran.</summary>
    public static string Tooltip(ScreenState state) => state switch
    {
        ScreenState.Ready => "Trofeo dashboard — écran : prêt",
        ScreenState.Recovering => "Trofeo dashboard — écran : reprise…",
        _ => "Trofeo dashboard — écran : non branché",
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _screen.StateChanged -= OnStateChanged;
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    // Levé sur le thread de la boucle d'affichage : on repasse par le thread de l'interface.
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_ui is null)
        {
            UpdateText();
            return;
        }

        _ui.Post(_ => UpdateText(), null);
    }

    private void UpdateText()
    {
        if (!_disposed) _icon.Text = Tooltip(_screen.State);
    }
}
