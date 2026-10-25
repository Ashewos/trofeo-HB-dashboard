using Trofeo.ModuleKit;

namespace Trofeo.Core.Shortcuts;

/// <summary>Les raccourcis pris quand la disposition n'en règle aucun.</summary>
public static class ShortcutDefaults
{
    /// <summary>Ctrl+Alt+Gauche et Ctrl+Alt+Droite pour changer d'onglet, Ctrl+Alt+Maj+1 à 9 pour aller à l'onglet 1 à 9.</summary>
    public static IReadOnlyList<ShortcutBinding> All { get; } =
    [
        new("Ctrl+Alt+Left", ModuleCommandKind.PreviousTab),
        new("Ctrl+Alt+Right", ModuleCommandKind.NextTab),
        // Maj en plus : sur un clavier AZERTY, AltGr vaut Ctrl+Alt, et Ctrl+Alt+chiffre volerait ~ # { [ | ` \ ^.
        .. Enumerable.Range(1, 9).Select(n => new ShortcutBinding($"Ctrl+Alt+Shift+{n}", ModuleCommandKind.SelectTab, n - 1)),
    ];
}
