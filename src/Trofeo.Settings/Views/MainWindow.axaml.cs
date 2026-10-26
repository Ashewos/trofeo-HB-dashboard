using Avalonia.Controls;

namespace Trofeo.Settings.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Ramène la fenêtre au premier plan (un second lancement l'a demandé).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }
}
