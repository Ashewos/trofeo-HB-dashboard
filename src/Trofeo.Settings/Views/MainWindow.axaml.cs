using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Trofeo.Settings.ViewModels;

namespace Trofeo.Settings.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Réduite, la fenêtre ne montre rien : l'aperçu se met en pause.
        if (change.Property == WindowStateProperty && DataContext is MainViewModel model)
            model.SetPreviewPaused(WindowState == WindowState.Minimized);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not MainViewModel { IsGridVisible: true } model)
            return;

        // Dans l'onglet Disposition : Échap abandonne le glissement, Ctrl+Z annule, Ctrl+Y (ou Ctrl+Maj+Z) rétablit.
        var layout = model.LayoutTab;
        if (e.Key == Key.Escape && layout.IsDragging)
        {
            layout.CancelDrag();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _ = layout.UndoAsync();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && (e.Key == Key.Y || e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _ = layout.RedoAsync();
            e.Handled = true;
        }
    }

    /// <summary>Ramène la fenêtre au premier plan (un second lancement l'a demandé).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }
}
