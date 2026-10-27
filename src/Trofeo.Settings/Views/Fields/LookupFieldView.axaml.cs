using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Trofeo.Settings.ViewModels.Fields;

namespace Trofeo.Settings.Views.Fields;

public partial class LookupFieldView : UserControl
{
    public LookupFieldView()
    {
        InitializeComponent();

        // Cherchable : toucher la zone ouvre la liste (au moins le choix du vide), Échap la referme, et la liste se referme
        // quand le focus quitte tout le champ (pas quand il passe de la zone à une ligne : le clic sur la ligne doit aboutir).
        Box.GotFocus += (_, _) => (DataContext as LookupFieldViewModel)?.Open();
        Box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is LookupFieldViewModel field)
            {
                field.Close();
                e.Handled = true;
            }
        };
        Root.AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        // Le focus n'a pas encore atteint sa nouvelle cible : on regarde où il est une fois le clic traité.
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is LookupFieldViewModel { IsSearchable: true } field && !HasFocusInside())
                field.Close();
        }, DispatcherPriority.Input);
    }

    private bool HasFocusInside() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && (focused == this || this.IsVisualAncestorOf(focused));
}
