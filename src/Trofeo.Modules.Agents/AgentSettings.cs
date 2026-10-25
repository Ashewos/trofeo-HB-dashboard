using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>Ce que le corps du module montre : le fil d'activité, ou la vue d'ensemble (action en cours et tâches).</summary>
public enum AgentView { Feed, Overview }

/// <summary>Où se trouve la ligne la plus récente du fil : en haut (les lignes descendent) ou en bas (elles montent, comme un terminal).</summary>
public enum FeedNewest { Top, Bottom }

/// <summary>
/// Réglages d'un module Claude Code / Codex, lus de
/// <c>{"view":"feed"|"overview","autoFollow":true,"feedFade":false,"feedNewest":"top"|"bottom"}</c>.
/// <paramref name="AutoFollow"/> : l'onglet affiché passe à la session qui se met à attendre l'utilisateur.
/// <paramref name="FeedFade"/> : les lignes du fil pâlissent à mesure qu'elles vieillissent (désactivé par défaut).
/// <paramref name="FeedNewest"/> : le côté du fil où arrive la ligne la plus récente.
/// </summary>
public sealed record AgentSettings(
    AgentView View = AgentView.Feed, bool AutoFollow = true, bool FeedFade = false, FeedNewest FeedNewest = FeedNewest.Top)
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

        var feedFade = false;
        if (json.TryGetProperty("feedFade", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            feedFade = f.GetBoolean();
        }

        var feedNewest = FeedNewest.Top;
        if (json.TryGetProperty("feedNewest", out var n) && n.ValueKind == JsonValueKind.String)
        {
            feedNewest = n.GetString()?.Trim().ToLowerInvariant() switch
            {
                "bottom" => FeedNewest.Bottom,
                _ => FeedNewest.Top,
            };
        }

        return new AgentSettings(view, autoFollow, feedFade, feedNewest);
    }
}
