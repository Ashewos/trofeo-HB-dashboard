namespace Trofeo.Modules.Agents;

/// <summary>Emplacement du dossier de configuration de Codex.</summary>
public static class CodexHome
{
    /// <summary><c>%CODEX_HOME%</c> s'il est défini, sinon <c>~/.codex</c>.</summary>
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }
}
