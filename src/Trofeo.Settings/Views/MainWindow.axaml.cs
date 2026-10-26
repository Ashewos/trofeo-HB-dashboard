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
        if (e.Handled || DataContext is not MainViewModel model)
            return;

        // Échap abandonne un glissement du damier.
        if (e.Key == Key.Escape && model.LayoutTab.IsDragging)
        {
            model.LayoutTab.CancelDrag();
            e.Handled = true;
            return;
        }

        // Ctrl+Z annule, Ctrl+Y (ou Ctrl+Maj+Z) rétablit : la dernière modification, de quelque onglet qu'elle vienne.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || model.LayoutTab.IsDragging)
            return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.Z && !shift)
        {
            _ = model.LayoutTab.UndoAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Y || e.Key == Key.Z && shift)
        {
            _ = model.LayoutTab.RedoAsync();
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
