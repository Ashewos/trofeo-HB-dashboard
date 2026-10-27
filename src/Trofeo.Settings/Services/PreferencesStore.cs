namespace Trofeo.Settings.Services;

/// <summary>
/// Les préférences de la fenêtre en cours, et leur fichier : un changement est écrit tout de suite (<c>settings-app.json</c>).
/// Sans chemin (tests, aperçus), rien n'est écrit.
/// </summary>
public sealed class PreferencesStore(EditorPreferences initial, string? path = null)
{
    public EditorPreferences Current { get; private set; } = initial;

    /// <summary>Une préférence vient de changer (l'assistant de première ouverture se rouvre ou se ferme d'après elle).</summary>
    public event Action? Changed;

    public void Update(Func<EditorPreferences, EditorPreferences> change)
    {
        Current = change(Current);
        if (path is not null)
            Current.Save(path);
        Changed?.Invoke();
    }
}
