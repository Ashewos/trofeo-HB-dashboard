using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Trofeo.Settings.Views;

public partial class LayoutTabView : UserControl
{
    public LayoutTabView() => InitializeComponent();

    /// <summary>Un type a été choisi dans la liste « Ajouter un module » : la liste se referme (la commande ajoute le module).</summary>
    private void OnAddTypeClicked(object? sender, RoutedEventArgs e) => AddButton.Flyout?.Hide();
}
