namespace Trofeo.Core;

/// <summary>Emplacements des fichiers de l'application.</summary>
public static class AppPaths
{
    /// <summary>Dossier des réglages : <c>%AppData%\trofeo-HB-dashboard</c>.</summary>
    public static string SettingsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "trofeo-HB-dashboard");
}
