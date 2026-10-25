namespace Trofeo.StatusLine;

/// <summary>Emplacement du dossier de configuration de Claude Code et de nos fichiers dedans.</summary>
public static class ClaudeHome
{
    /// <summary>
    /// <c>%CLAUDE_CONFIG_DIR%</c> s'il est défini, sinon <c>~/.claude</c>. On écrit sous ce dossier (et non dans
    /// <c>%AppData%</c>) car l'appli Claude empaquetée redirige <c>%AppData%</c> vers une copie privée.
    /// </summary>
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    }

    /// <summary>Dossier des captures : <c>&lt;claudeHome&gt;/trofeo-HB-dashboard/statusline</c>.</summary>
    public static string StatusLineDir(string claudeHome) => Path.Combine(claudeHome, "trofeo-HB-dashboard", "statusline");
}
