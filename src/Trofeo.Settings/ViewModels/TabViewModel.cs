using CommunityToolkit.Mvvm.ComponentModel;

namespace Trofeo.Settings.ViewModels;

/// <summary>Un onglet de la fenêtre. Les tâches 10 et 11 ont rempli Disposition et Personnalisation ; la tâche 12 a rempli Modules ; la tâche 13 a rempli Raccourcis et Paramètres.</summary>
public abstract class TabViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
}
