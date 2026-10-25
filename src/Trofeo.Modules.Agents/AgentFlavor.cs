namespace Trofeo.Modules.Agents;

/// <summary>Ce qui distingue Claude Code de Codex dans le module : deux textes d'écran et la mascotte.</summary>
/// <param name="NoSessionText">Texte quand l'outil n'a aucune session ouverte.</param>
/// <param name="NotInstalledText">Message d'action quand l'outil n'est pas installé sur le PC.</param>
/// <param name="Mascot">La mascotte de l'outil.</param>
public sealed record AgentFlavor(string NoSessionText, string NotInstalledText, MascotDefinition Mascot);
