using Avalonia.Input;
using Trofeo.Core.Shortcuts;

namespace Trofeo.Settings.Services;

/// <summary>
/// Traduit les touches d'Avalonia dans la syntaxe de <see cref="ShortcutKeys"/> (« Ctrl+Alt+Right ») et écrit cette syntaxe
/// en pastille (« Ctrl + Alt + → »). Seules les touches que <see cref="ShortcutKeys.TryParse"/> lit sont prises : le service
/// ne pourrait pas enregistrer les autres.
/// </summary>
public static class ShortcutCapture
{
    /// <summary>
    /// Les touches pressées en syntaxe de <see cref="ShortcutKeys"/> (modificateurs dans l'ordre Ctrl, Alt, Shift, Win, puis
    /// la touche), ou <c>null</c> : aucun modificateur, une lettre ou un chiffre sans Alt ni Win (<see cref="NeedsAltOrWin"/>), ou
    /// une touche que le service ne sait pas enregistrer.
    /// </summary>
    public static string? Translate(Key key, KeyModifiers modifiers)
    {
        var name = KeyName(key);
        if (name is null || NeedsAltOrWin(key, modifiers))
            return null;

        var words = new List<string>(5);
        if (modifiers.HasFlag(KeyModifiers.Control)) words.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) words.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) words.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Meta)) words.Add("Win");
        if (words.Count == 0)
            return null; // une touche seule volerait la frappe de tout Windows : ShortcutKeys exige un modificateur

        words.Add(name);
        var keys = string.Join('+', words);
        return ShortcutKeys.TryParse(keys, out _, out _) ? keys : null;
    }

    /// <summary>
    /// Une lettre ou un chiffre pressé avec Ctrl ou Maj, sans Alt ni Win : Ctrl+C ou Maj+A en raccourci global casserait la
    /// saisie dans toutes les applications. Les flèches et les touches F gardent la règle d'avant (un modificateur suffit) ;
    /// sans aucun modificateur, il n'y a rien à expliquer : la capture attend.
    /// </summary>
    public static bool NeedsAltOrWin(Key key, KeyModifiers modifiers) =>
        IsLetterOrDigit(key)
        && (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift))
        && !modifiers.HasFlag(KeyModifiers.Alt) && !modifiers.HasFlag(KeyModifiers.Meta);

    private static bool IsLetterOrDigit(Key key) => key is >= Key.D0 and <= Key.D9 or >= Key.A and <= Key.Z;

    /// <summary>La touche est un modificateur seul (Ctrl, Alt, Maj, Windows) : on attend la touche qui l'accompagne.</summary>
    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>« Ctrl+Alt+Right » devient « Ctrl + Alt + → » ; Shift s'écrit « Maj », comme sur un clavier français.</summary>
    public static string Display(string keys) =>
        string.Join(" + ", keys.Split('+', StringSplitOptions.TrimEntries).Select(Word));

    private static string Word(string word) => word.ToUpperInvariant() switch
    {
        "LEFT" => "←",
        "RIGHT" => "→",
        "UP" => "↑",
        "DOWN" => "↓",
        "CTRL" => "Ctrl",
        "ALT" => "Alt",
        "SHIFT" => "Maj",
        "WIN" => "Win",
        var upper => upper,
    };

    /// <summary>Le nom de la touche dans la syntaxe de <see cref="ShortcutKeys"/> ; <c>null</c> si elle n'y figure pas.</summary>
    private static string? KeyName(Key key)
    {
        switch (key)
        {
            case Key.Left: return "Left";
            case Key.Right: return "Right";
            case Key.Up: return "Up";
            case Key.Down: return "Down";
        }

        // Le rang du chiffre de la rangée du haut (D0 à D9) ; le pavé numérique a d'autres codes de touche virtuelle.
        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();
        if (key is >= Key.A and <= Key.Z)
            return ((char)('A' + (key - Key.A))).ToString();
        if (key is >= Key.F1 and <= Key.F24)
            return "F" + (key - Key.F1 + 1);
        return null;
    }
}
