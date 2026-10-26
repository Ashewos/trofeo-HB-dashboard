using CommunityToolkit.Mvvm.ComponentModel;

namespace Trofeo.Settings.ViewModels;

/// <summary>Un onglet de la fenêtre. Les tâches 10 à 13 remplissent chacun des cinq.</summary>
public abstract class TabViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
}

public sealed class LayoutTabViewModel() : TabViewModel(Texts.TabLayout);

public sealed class AppearanceTabViewModel() : TabViewModel(Texts.TabAppearance);

public sealed class ModulesTabViewModel() : TabViewModel(Texts.TabModules);

public sealed class ShortcutsTabViewModel() : TabViewModel(Texts.TabShortcuts);

public sealed class SettingsTabViewModel() : TabViewModel(Texts.TabSettings);
