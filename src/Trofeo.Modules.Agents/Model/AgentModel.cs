namespace Trofeo.Modules.Agents;

/// <summary>Ce que fait une session en ce moment.</summary>
public enum AgentActivity { Idle, Working, Waiting }

/// <summary>Avancement d'une tâche de la liste de l'agent.</summary>
public enum AgentTaskState { Pending, InProgress, Completed }

/// <summary>Une ligne du fil d'activité : l'outil utilisé, sa cible, et les lignes ajoutées / retirées s'il y en a.</summary>
public sealed record FeedEntry(string Tool, string Target, int? Added = null, int? Removed = null);

/// <summary>Une tâche de la liste de l'agent.</summary>
public sealed record AgentTask(string Text, AgentTaskState State);

/// <summary>Une fenêtre de quota : part consommée (en %) et moment de remise à zéro, s'il est connu.</summary>
public sealed record QuotaWindow(double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>Quotas de l'outil (fenêtre de 5 h et hebdomadaire), avec l'instant de leur relevé.</summary>
public sealed record AgentQuotas(QuotaWindow? FiveHour, QuotaWindow? Weekly, DateTimeOffset CapturedAt);

/// <summary>Résumé d'une session, de quoi dessiner son onglet.</summary>
public sealed record AgentSessionSummary(string Id, string ProjectName, AgentActivity Activity,
    DateTimeOffset StartedAt, DateTimeOffset LastActivityAt);

/// <summary>
/// Détail de la session choisie. <paramref name="Feed"/> va du plus ancien au plus récent, 50 entrées au plus ;
/// <paramref name="Tasks"/> est vide quand l'agent n'a pas de liste.
/// <paramref name="TaskFinishedAt"/> fige la durée d'un traitement terminé ; null conserve le chronomètre en cours.
/// </summary>
public sealed record AgentSessionDetail(string Id, string? CurrentTask, DateTimeOffset? TaskStartedAt,
    IReadOnlyList<FeedEntry> Feed, IReadOnlyList<AgentTask> Tasks, string? Model, double? ContextPercent,
    DateTimeOffset? TaskFinishedAt = null);

/// <summary>
/// Photo de l'état d'un outil. <paramref name="Installed"/> : l'outil est présent sur le PC (son dossier existe).
/// Les sessions sont triées par <see cref="AgentSessionSummary.StartedAt"/> croissant.
/// </summary>
public sealed record AgentSnapshot(bool Installed, IReadOnlyList<AgentSessionSummary> Sessions,
    AgentSessionDetail? Selected, AgentQuotas? Quotas);
