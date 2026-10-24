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
            Text = CurrentTooltip(),
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // S'abonner d'abord, lire l'état ensuite : un « prêt » atteint pendant la construction n'est pas perdu.
        screen.StateChanged += OnStateChanged;
        _icon.Text = CurrentTooltip();
    }

    /// <summary>Longueur maximale d'une info-bulle de la zone de notification (127 caractères + le zéro final).</summary>
    private const int MaxTooltipLength = 127;

    /// <summary>Texte de l'info-bulle : l'état de l'écran et, quand il est déconnecté, le motif.</summary>
    public static string Tooltip(ScreenState state, string? reason)
    {
        var text = state switch
        {
            ScreenState.Ready => "Trofeo dashboard — écran : prêt",
            ScreenState.Recovering => "Trofeo dashboard — écran : reprise…",
            _ => "Trofeo dashboard — écran : " + (reason ?? "non branché"),
        };

        return text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }

    private string CurrentTooltip() => Tooltip(_screen.State, _screen.Reason);

    protected override void ExitThreadCore()
    {
        _screen.StateChanged -= OnStateChanged; // plus rien à poster une fois la boucle d'interface terminée
        base.ExitThreadCore();
    }

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

        try
        {
            _ui.Post(_ => UpdateText(), null);
        }
        catch (InvalidOperationException)
        {
            // La boucle d'interface est terminée : plus d'info-bulle à mettre à jour.
        }
    }

    private void UpdateText()
    {
        if (!_disposed) _icon.Text = CurrentTooltip();
    }
}
