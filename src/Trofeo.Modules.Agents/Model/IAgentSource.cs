namespace Trofeo.Modules.Agents;

/// <summary>
/// Lit l'état d'un outil (Claude Code, Codex) sur disque. Appelé depuis un seul thread à la fois ; peut lever une
/// exception, que le module traite.
/// </summary>
public interface IAgentSource : IDisposable
{
    /// <summary>Relève l'état ; <paramref name="selectedSessionId"/> désigne la session dont on veut le détail.</summary>
    AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset now);
}
