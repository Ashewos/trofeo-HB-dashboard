using System.Text.Json;

namespace Trofeo.ModuleKit;

/// <summary>Fabrique d'un module qui décrit ses réglages à la fenêtre de réglage.</summary>
public interface IConfigurableModuleFactory : IModuleFactory
{
    SettingsSchema Schema { get; }

    /// <summary>Propositions d'un champ <see cref="LookupField"/> pour la requête donnée.</summary>
    Task<IReadOnlyList<LookupResult>> LookupAsync(string fieldKey, string query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LookupResult>>([]);

    /// <summary>Lance l'action d'un champ <see cref="ActionField"/> avec les réglages courants.</summary>
    Task<ActionResult> RunActionAsync(string actionKey, JsonElement settings, ISecretStore secrets, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "Action inconnue."));
}
