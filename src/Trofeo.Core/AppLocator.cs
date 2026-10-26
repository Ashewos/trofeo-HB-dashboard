namespace Trofeo.Core;

/// <summary>
/// Retrouve les programmes et dossiers voisins (Trofeo.Display.exe, Trofeo.Settings.exe, Trofeo.SensorHelper.exe, le dossier
/// <c>mods</c>) : à côté de l'application installée, ou dans les sorties de compilation d'un dépôt. Ne lève jamais.
/// </summary>
public static class AppLocator
{
    private const string TargetFramework = "net10.0-windows10.0.19041.0";
    private static readonly string[] Configurations = ["Release", "Debug"];

    /// <summary>
    /// Le chemin du fichier <paramref name="fileName"/>, ou <c>null</c>. Cherche dans le dossier de départ (par défaut celui de
    /// l'application), puis en remontant d'au plus <paramref name="maxLevels"/> dossiers : dans les sorties
    /// <c>src/&lt;projet&gt;/bin/{Release,Debug}/net10.0-windows10.0.19041.0/</c> d'un dépôt (la Release d'abord), puis dans le
    /// dossier lui-même.
    /// </summary>
    public static string? Find(string fileName, string? startDir = null, int maxLevels = 8)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(startDir ?? AppContext.BaseDirectory));
            for (var level = 0; dir is not null && level <= maxLevels; level++, dir = dir.Parent)
            {
                var found = level == 0
                    ? InFolder(dir, fileName)
                    : InRepoOutputs(dir, fileName) ?? InFolder(dir, fileName);
                if (found is not null) return found;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Dossier illisible ou chemin invalide : introuvable.
        }
        return null;
    }

    /// <summary>
    /// Le dossier <paramref name="relativePath"/> (« mods ») du dossier de départ ou, en remontant d'au plus
    /// <paramref name="maxLevels"/> dossiers, du premier parent qui le contient ; <c>null</c> s'il n'existe nulle part.
    /// </summary>
    public static string? FindDirectory(string relativePath, string? startDir = null, int maxLevels = 8)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(startDir ?? AppContext.BaseDirectory));
            for (var level = 0; dir is not null && level <= maxLevels; level++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relativePath);
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
        return null;
    }

    private static string? InFolder(DirectoryInfo dir, string fileName)
    {
        var path = Path.Combine(dir.FullName, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Les sorties de compilation des projets de <c>src</c> si <paramref name="repo"/> est la racine d'un dépôt.</summary>
    private static string? InRepoOutputs(DirectoryInfo repo, string fileName)
    {
        var src = Path.Combine(repo.FullName, "src");
        if (!Directory.Exists(src)) return null;

        var projects = Directory.GetDirectories(src);
        Array.Sort(projects, StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in Configurations)
        {
            foreach (var project in projects)
            {
                var path = Path.Combine(project, "bin", configuration, TargetFramework, fileName);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }
}
