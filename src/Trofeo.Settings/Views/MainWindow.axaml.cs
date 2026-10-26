using Avalonia;
using Avalonia.Controls;
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

    /// <summary>Ramène la fenêtre au premier plan (un second lancement l'a demandé).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }
}
