namespace Trofeo.Modules.Agents;

/// <summary>Ce qui distingue Claude Code de Codex : textes, mascotte et sens des pourcentages affichés.</summary>
/// <param name="NoSessionText">Texte quand l'outil n'a aucune session ouverte.</param>
/// <param name="NotInstalledText">Message d'action quand l'outil n'est pas installé sur le PC.</param>
/// <param name="Mascot">La mascotte de l'outil.</param>
/// <param name="DisplayRemainingPercent">Afficher le contexte et les quotas restants plutôt que consommés.</param>
public sealed record AgentFlavor(string NoSessionText, string NotInstalledText, MascotDefinition Mascot,
    bool DisplayRemainingPercent = false);
