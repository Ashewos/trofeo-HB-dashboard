using CommunityToolkit.Mvvm.ComponentModel;

namespace Trofeo.Settings.ViewModels;

/// <summary>Un onglet de la fenêtre. Les tâches 10 et 11 ont rempli Disposition et Personnalisation ; les tâches 12 et 13 remplissent les autres.</summary>
public abstract class TabViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
}

public sealed class ModulesTabViewModel() : TabViewModel(Texts.TabModules);

public sealed class ShortcutsTabViewModel() : TabViewModel(Texts.TabShortcuts);

public sealed class SettingsTabViewModel() : TabViewModel(Texts.TabSettings);
