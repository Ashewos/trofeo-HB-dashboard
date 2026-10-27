using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Trofeo.Settings.Services;
using Trofeo.Settings.ViewModels;

namespace Trofeo.Settings.Views;

public partial class SettingsTabView : UserControl
{
    public SettingsTabView()
    {
        InitializeComponent();

        // Le plafond d'images ne s'enregistre qu'au lâcher (souris ou doigt, fin du glissement de la poignée, touche relâchée) :
        // un seul pas d'annulation, et pas un rechargement du service par cran de glissement. Ces événements sont parfois
        // déjà traités par le curseur lui-même : on les écoute quand même.
        FpsSlider.AddHandler(PointerReleasedEvent, OnFpsReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        FpsSlider.AddHandler(Thumb.DragCompletedEvent, OnFpsReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        FpsSlider.AddHandler(KeyUpEvent, OnFpsReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Le sélecteur de fichier est celui de la fenêtre qui porte l'onglet.
        StorageFilePicker.EnsureFor(this, DataContext as SettingsTabViewModel);
    }

    private void OnFpsReleased(object? sender, RoutedEventArgs e) =>
        _ = (DataContext as SettingsTabViewModel)?.CommitMaxFpsAsync();
}
