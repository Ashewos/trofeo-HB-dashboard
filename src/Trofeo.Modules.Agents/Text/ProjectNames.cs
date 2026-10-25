namespace Trofeo.Modules.Agents;

/// <summary>Nom du projet affiché sur un onglet, déduit du dossier de travail de la session.</summary>
public static class ProjectNames
{
    private const string WorktreeMarker = "/.git/worktrees/";

    /// <summary>
    /// Remonte depuis <paramref name="cwd"/> jusqu'au premier <c>.git</c>. Un dossier <c>.git</c> donne le nom du dossier
    /// qui le contient ; un fichier <c>gitdir:</c> d'arbre de travail donne celui du dépôt principal. Sans <c>.git</c>
    /// (ou s'il est illisible), c'est le nom du dossier <paramref name="cwd"/> lui-même. Les séparateurs <c>\</c> et
    /// <c>/</c> sont acceptés.
    /// </summary>
    public static string FromWorkingDirectory(string cwd)
    {
        var start = cwd.Replace('\\', '/').TrimEnd('/');
        if (start.Length == 0 || IsDriveRoot(start))
        {
            return cwd;
        }

        for (string? dir = start; dir is not null; dir = Parent(dir))
        {
            var git = dir + "/.git";
            if (Directory.Exists(git))
            {
                return LastSegment(dir);
            }

            if (File.Exists(git))
            {
                return FromGitFile(git, dir) ?? LastSegment(start);
            }
        }

        return LastSegment(start);
    }

    /// <summary>Nom du dépôt principal d'après un fichier <c>.git</c> ; <c>null</c> si le fichier est illisible ou vide.</summary>
    private static string? FromGitFile(string gitFile, string dir)
    {
        string content;
        try
        {
            content = File.ReadAllText(gitFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        const string prefix = "gitdir:";
        var line = content.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var gitdir = line[prefix.Length..].Trim().Replace('\\', '/');
        if (!IsRooted(gitdir))
        {
            gitdir = Path.GetFullPath(Path.Combine(dir, gitdir)).Replace('\\', '/');
        }

        var marker = gitdir.IndexOf(WorktreeMarker, StringComparison.Ordinal);
        // Pas un arbre de travail (sous-module…) : le projet est le dossier qui porte le .git.
        return marker > 0 ? LastSegment(gitdir[..marker]) : LastSegment(dir);
    }

    private static bool IsDriveRoot(string path) => path.Length == 2 && path[1] == ':';

    private static bool IsRooted(string path) =>
        path.StartsWith('/') || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');

    private static string? Parent(string dir)
    {
        var slash = dir.LastIndexOf('/');
        return slash < 0 ? null : dir[..slash];
    }

    private static string LastSegment(string path) => path[(path.LastIndexOf('/') + 1)..];
}
