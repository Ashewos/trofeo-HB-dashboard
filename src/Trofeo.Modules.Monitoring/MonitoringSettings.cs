using System.Text.Json;

namespace Trofeo.Modules.Monitoring;

/// <summary>
/// Réglages du module Monitoring PC, lus de <c>{"title":"...","rows":["cpu","gpu","ram"]}</c>.
/// <paramref name="Title"/> vaut <c>null</c> quand aucun titre n'est choisi (le nom du matériel est alors affiché).
/// </summary>
public sealed record MonitoringSettings(string? Title, IReadOnlyList<string> Rows)
{
    /// <summary>Lignes affichées quand les réglages n'en donnent aucune de valide.</summary>
    public static readonly IReadOnlyList<string> DefaultRows = ["cpu", "gpu", "ram"];

    /// <summary>
    /// Lit les réglages sans jamais lever : une forme inattendue donne les valeurs par défaut, les identifiants
    /// de ligne inconnus ou qui ne sont pas du texte sont ignorés, un titre vide ou blanc vaut <c>null</c>.
    /// </summary>
    public static MonitoringSettings FromJson(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return new MonitoringSettings(null, DefaultRows);

        string? title = null;
        if (settings.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
        {
            var text = t.GetString()?.Trim();
            if (!string.IsNullOrEmpty(text)) title = text;
        }

        var rows = new List<string>();
        if (settings.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in r.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var id = item.GetString()?.Trim().ToLowerInvariant();
                if (id is not null && MonitoringRows.IsKnown(id)) rows.Add(id);
            }
        }

        return new MonitoringSettings(title, rows.Count > 0 ? rows : DefaultRows);
    }
}
