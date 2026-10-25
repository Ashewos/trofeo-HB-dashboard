using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>Ce que le corps du module montre : le fil d'activité, ou la vue d'ensemble (action en cours et tâches).</summary>
public enum AgentView { Feed, Overview }

/// <summary>
/// Réglages d'un module Claude Code / Codex, lus de <c>{"view":"feed"|"overview","autoFollow":true}</c>.
/// <paramref name="AutoFollow"/> : l'onglet affiché passe à la session qui se met à attendre l'utilisateur.
/// </summary>
public sealed record AgentSettings(AgentView View = AgentView.Feed, bool AutoFollow = true)
{
    /// <summary>Lit les réglages sans jamais lever : tout ce qui manque ou n'est pas reconnu garde sa valeur par défaut.</summary>
    public static AgentSettings Parse(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return new AgentSettings();
        }

        var view = AgentView.Feed;
        if (json.TryGetProperty("view", out var v) && v.ValueKind == JsonValueKind.String)
        {
            view = v.GetString()?.Trim().ToLowerInvariant() switch
            {
                "overview" => AgentView.Overview,
                _ => AgentView.Feed,
            };
        }

        var autoFollow = true;
        if (json.TryGetProperty("autoFollow", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            autoFollow = a.GetBoolean();
        }

        return new AgentSettings(view, autoFollow);
    }
}
