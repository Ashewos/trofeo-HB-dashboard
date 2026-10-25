using System.Text;
using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Sonde de format : décrit la structure d'une transcription Codex (les clés et le genre JSON de leur valeur, groupées par
/// <c>type/payload.type</c>) pour qu'on puisse vérifier le format attendu sur une vraie transcription. Ne sort jamais une
/// valeur : ni chaîne, ni nombre, ni booléen. Une clé ou un type qui n'a pas l'allure d'un identifiant (une donnée de
/// l'utilisateur qui serait utilisée comme clé) est remplacé par « ? ».
/// </summary>
public static class CodexProbe
{
    /// <summary>Profondeur maximale des clés décrites : <c>payload.rate_limits.primary.used_percent</c> est à la profondeur 3.</summary>
    private const int MaxDepth = 3;
    private const int MaxGroups = 100;
    private const int MaxPathsPerGroup = 300;
    private const int MaxNameLength = 64;

    private sealed class Group
    {
        public int Lines { get; set; }

        public Dictionary<string, SortedSet<string>> Paths { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Le rapport : pour chaque couple <c>type/payload.type</c>, le nombre de lignes et les clés avec leur genre.</summary>
    public static string Describe(IEnumerable<string> jsonLines)
    {
        var groups = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        var unreadable = 0;
        foreach (var line in jsonLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    unreadable++;
                    continue;
                }

                var key = GroupKey(root);
                if (!groups.TryGetValue(key, out var group))
                {
                    if (groups.Count >= MaxGroups)
                    {
                        continue;
                    }

                    group = new Group();
                    groups[key] = group;
                }

                group.Lines++;
                Walk(root, "", 0, group);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                unreadable++;
            }
        }

        var text = new StringBuilder();
        foreach (var (key, group) in groups)
        {
            text.Append(key).Append(" : ").Append(group.Lines).Append(group.Lines > 1 ? " lignes" : " ligne").Append('\n');
            foreach (var (path, kinds) in group.Paths.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                text.Append("  ").Append(path).Append(" : ").Append(string.Join('|', kinds)).Append('\n');
            }
        }

        if (groups.Count == 0)
        {
            text.Append("Aucune ligne lisible.\n");
        }

        if (unreadable > 0)
        {
            text.Append(unreadable).Append(unreadable > 1 ? " lignes illisibles" : " ligne illisible").Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary><c>type/payload.type</c> (ou seulement <c>type</c>) ; « ? » quand l'un manque ou n'a pas l'allure d'un identifiant.</summary>
    private static string GroupKey(JsonElement root)
    {
        var type = Identifier(root, "type");
        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("type", out _))
        {
            return type + "/" + Identifier(payload, "type");
        }

        return type;
    }

    private static string Identifier(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text && IsIdentifier(text)
            ? text
            : "?";

    private static bool IsIdentifier(string text) =>
        text.Length is > 0 and <= MaxNameLength && text.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.');

    private static void Walk(JsonElement element, string path, int depth, Group group)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Visit(property.Value, Join(path, IsIdentifier(property.Name) ? property.Name : "?"), depth, group);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Visit(item, path + "[]", depth, group);
            }
        }
    }

    /// <summary>Note le genre de la valeur à ce chemin, puis descend dedans tant que la profondeur le permet.</summary>
    private static void Visit(JsonElement value, string path, int depth, Group group)
    {
        if (!group.Paths.TryGetValue(path, out var kinds))
        {
            if (group.Paths.Count >= MaxPathsPerGroup)
            {
                return;
            }

            kinds = [];
            group.Paths[path] = kinds;
        }

        kinds.Add(value.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => "array",
            _ => "object",
        });

        if (depth < MaxDepth)
        {
            Walk(value, path, depth + 1, group);
        }
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;
}
