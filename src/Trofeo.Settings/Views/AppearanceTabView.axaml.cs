using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Trofeo.Settings.Services;
using Trofeo.Settings.ViewModels;

namespace Trofeo.Settings.Views;

public partial class AppearanceTabView : UserControl
{
    public AppearanceTabView()
    {
        InitializeComponent();

        // La transparence ne s'enregistre qu'au lâcher (souris ou doigt, fin du glissement de la poignée, touche relâchée) :
        // un seul pas d'annulation, et pas un rechargement du service par pixel de glissement. Ces événements sont parfois
        // déjà traités par le curseur lui-même : on les écoute quand même.
        OpacitySlider.AddHandler(PointerReleasedEvent, OnOpacityReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        OpacitySlider.AddHandler(Thumb.DragCompletedEvent, OnOpacityReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        OpacitySlider.AddHandler(KeyUpEvent, OnOpacityReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        // Un champ de couleur quitté est validé comme avec Entrée.
        HexBox.LostFocus += (_, _) => (DataContext as AppearanceTabViewModel)?.ApplyColorTextCommand.Execute(null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Le sélecteur de fichier est celui de la fenêtre qui porte l'onglet.
        if (DataContext is AppearanceTabViewModel { Picker: null } model && TopLevel.GetTopLevel(this) is { } top)
        {
            model.Picker = new StorageFilePicker(top);
        }
    }

    private void OnOpacityReleased(object? sender, RoutedEventArgs e) =>
        _ = (DataContext as AppearanceTabViewModel)?.CommitOpacityAsync();
}
