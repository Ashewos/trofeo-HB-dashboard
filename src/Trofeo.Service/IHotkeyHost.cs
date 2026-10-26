using Trofeo.Core.Shortcuts;

namespace Trofeo.Display;

/// <summary>
/// Ce qui pose les raccourcis clavier globaux : la fenêtre des raccourcis du service, sur le fil de l'interface. Un faux
/// la remplace dans les tests.
/// </summary>
public interface IHotkeyHost
{
    /// <summary>Remplace tous les raccourcis. Rend les raccourcis qui n'ont pas pu être posés, en français ; vide si tout va bien.</summary>
    Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<ShortcutBinding> bindings);
}
