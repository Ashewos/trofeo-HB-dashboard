namespace Trofeo.Modules.Agents;

/// <summary>Retrouve le fichier de transcription d'une session Claude Code dans <c>&lt;claudeHome&gt;/projects</c>.</summary>
public static class TranscriptLocator
{
    /// <summary>
    /// Cherche <c>projects/&lt;cwd codé&gt;/&lt;sessionId&gt;.jsonl</c> (chaque caractère de <paramref name="cwd"/> hors
    /// <c>[A-Za-z0-9]</c> devient « - »), sinon <c>projects/*/&lt;sessionId&gt;.jsonl</c> quand le codage diffère. <c>null</c>
    /// si la transcription n'existe pas (encore) ou si l'identifiant n'est pas sûr comme nom de fichier.
    /// </summary>
    public static string? Find(string projectsDir, string cwd, string sessionId)
    {
        if (!ClaudeSessionDirectory.IsSafeSessionId(sessionId))
        {
            return null;
        }

        var fileName = sessionId + ".jsonl";
        try
        {
            var direct = Path.Combine(projectsDir, Encode(cwd), fileName);
            if (File.Exists(direct))
            {
                return direct;
            }

            if (!Directory.Exists(projectsDir))
            {
                return null;
            }

            foreach (var folder in Directory.EnumerateDirectories(projectsDir))
            {
                var candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Dossier verrouillé ou chemin inutilisable : pas de transcription pour l'instant.
        }

        return null;
    }

    /// <summary>Le nom de dossier que Claude Code donne à un dossier de travail.</summary>
    private static string Encode(string cwd) =>
        string.Create(cwd.Length, cwd, (span, text) =>
        {
            for (var i = 0; i < text.Length; i++)
            {
                span[i] = char.IsAsciiLetterOrDigit(text[i]) ? text[i] : '-';
            }
        });
}
