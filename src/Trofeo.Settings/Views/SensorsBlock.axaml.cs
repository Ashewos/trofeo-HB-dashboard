using Avalonia;
using Avalonia.Controls;
using Trofeo.Settings.Services;
using Trofeo.Settings.ViewModels;

namespace Trofeo.Settings.Views;

/// <summary>
/// L'état des températures du processeur et le bouton d'installation du pilote, avec le modèle de l'onglet Paramètres :
/// l'onglet et l'assistant de première ouverture montrent le même bloc.
/// </summary>
public partial class SensorsBlock : UserControl
{
    /// <summary>Le bouton d'installation est le bouton principal (Accent). Faux dans l'assistant, où « Continuer » l'est.</summary>
    public static readonly StyledProperty<bool> PrimaryProperty = AvaloniaProperty.Register<SensorsBlock, bool>(nameof(Primary), true);

    public SensorsBlock() => InitializeComponent();

    public bool Primary
    {
        get => GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Sans PawnIO, l'installation demande son installeur : le sélecteur de fichier est celui de la fenêtre.
        StorageFilePicker.EnsureFor(this, DataContext as SettingsTabViewModel);
    }
}
