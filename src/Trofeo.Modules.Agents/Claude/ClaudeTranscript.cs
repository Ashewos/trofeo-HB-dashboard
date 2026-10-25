using System.Globalization;
using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>
/// État d'une session Claude Code déduit de sa transcription (<c>projects/*/&lt;sessionId&gt;.jsonl</c>), ligne après ligne.
/// Pas de verrou : à utiliser depuis un seul thread.
/// </summary>
public sealed class ClaudeTranscript
{
    private const int MaxFeed = 50;

    private readonly List<FeedEntry> _feed = [];
    private readonly List<string?> _feedIds = [];

    /// <summary>La dernière demande de l'utilisateur (sa première ligne non vide).</summary>
    public string? CurrentTask { get; private set; }

    /// <summary>Le moment de cette demande.</summary>
    public DateTimeOffset? TaskStartedAt { get; private set; }

    /// <summary>Les derniers outils utilisés, du plus ancien au plus récent, 50 au plus.</summary>
    public IReadOnlyList<FeedEntry> Feed => _feed;

    /// <summary>Le modèle du dernier message de l'assistant.</summary>
    public string? Model { get; private set; }

    /// <summary>La taille du contexte au dernier message de l'assistant, en jetons.</summary>
    public long? ContextTokens { get; private set; }

    /// <summary>La dernière liste écrite par l'outil TodoWrite, ou <c>null</c> s'il n'y en a pas eu.</summary>
    public IReadOnlyList<AgentTask>? Todos { get; private set; }

    /// <summary>Oublie tout (le fichier a été remplacé, on le relit depuis la fin).</summary>
    public void Reset()
    {
        CurrentTask = null;
        TaskStartedAt = null;
        _feed.Clear();
        _feedIds.Clear();
        Model = null;
        ContextTokens = null;
        Todos = null;
    }

    /// <summary>Prend en compte une ligne de la transcription. Une ligne vide, illisible ou inconnue est ignorée.</summary>
    public void Apply(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || IsTrue(root, "isSidechain"))
            {
                return;
            }

            switch (Str(root, "type"))
            {
                case "user":
                    ApplyUser(root);
                    break;
                case "assistant":
                    ApplyAssistant(root);
                    break;
            }
        }
        catch (JsonException)
        {
            // Ligne tronquée ou abîmée : on passe à la suivante.
        }
    }

    private void ApplyUser(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return;
        }

        if (content.ValueKind == JsonValueKind.Array && HasBlock(content, "tool_result"))
        {
            ApplyToolResult(root, content);
            return;
        }

        if (IsTrue(root, "isMeta"))
        {
            return;
        }

        var text = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join('\n', content.EnumerateArray()
                .Where(block => block.ValueKind == JsonValueKind.Object && Str(block, "type") == "text")
                .Select(block => Str(block, "text"))),
            _ => null,
        };

        // Les enveloppes de commandes (<command-name>…) et les rappels du système commencent par « < ».
        if (text is null || text.Trim().StartsWith('<'))
        {
            return;
        }

        var firstLine = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        if (firstLine is null)
        {
            return;
        }

        CurrentTask = ToolTargets.OneLine(firstLine);
        TaskStartedAt = DateTimeOffset.TryParse(Str(root, "timestamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var started) ? started : null;
    }

    private void ApplyToolResult(JsonElement root, JsonElement content)
    {
        // Le résultat « structuré » de l'outil ne porte pas d'identifiant : il va au premier tool_result du message.
        if (!root.TryGetProperty("toolUseResult", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = content.EnumerateArray()
            .Where(block => block.ValueKind == JsonValueKind.Object && Str(block, "type") == "tool_result")
            .Select(block => Str(block, "tool_use_id"))
            .FirstOrDefault();
        var index = id is null ? -1 : _feedIds.LastIndexOf(id);
        if (index < 0)
        {
            return;
        }

        // Une création de fichier a déjà ses lignes (comptées sur le contenu envoyé à l'outil).
        if (Str(result, "type") == "create"
            || !result.TryGetProperty("structuredPatch", out var patch) || patch.ValueKind != JsonValueKind.Array
            || patch.GetArrayLength() == 0)
        {
            return;
        }

        int added = 0, removed = 0;
        foreach (var hunk in patch.EnumerateArray())
        {
            if (hunk.ValueKind != JsonValueKind.Object || !hunk.TryGetProperty("lines", out var lines)
                || lines.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var line in lines.EnumerateArray())
            {
                var text = line.ValueKind == JsonValueKind.String ? line.GetString() : null;
                if (text is { Length: > 0 })
                {
                    if (text[0] == '+')
                    {
                        added++;
                    }
                    else if (text[0] == '-')
                    {
                        removed++;
                    }
                }
            }
        }

        _feed[index] = _feed[index] with { Added = added, Removed = removed };
    }

    private void ApplyAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var model = Str(message, "model");
        if (model == "<synthetic>")
        {
            return;
        }

        if (model is not null)
        {
            Model = model;
        }

        if (message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            ContextTokens = Long(usage, "input_tokens") + Long(usage, "cache_creation_input_tokens")
                + Long(usage, "cache_read_input_tokens");
        }

        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || Str(block, "type") != "tool_use")
            {
                continue;
            }

            var name = Str(block, "name");
            if (name is null)
            {
                continue;
            }

            var input = block.TryGetProperty("input", out var value) ? value : default;
            if (name == "TodoWrite")
            {
                ApplyTodos(input);
                continue;
            }

            var id = Str(block, "id");
            if (id is not null && _feedIds.Contains(id))
            {
                continue;
            }

            if (ToolTargets.Describe(name, input) is not var (tool, target))
            {
                continue;
            }

            _feed.Add(new FeedEntry(tool, target, Added: name == "Write" ? CountLines(Str(input, "content")) : null));
            _feedIds.Add(id);
            if (_feed.Count > MaxFeed)
            {
                _feed.RemoveAt(0);
                _feedIds.RemoveAt(0);
            }
        }
    }

    private void ApplyTodos(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("todos", out var todos)
            || todos.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var list = new List<AgentTask>();
        foreach (var todo in todos.EnumerateArray())
        {
            if (todo.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var state = Str(todo, "status") switch
            {
                "pending" => AgentTaskState.Pending,
                "in_progress" => AgentTaskState.InProgress,
                "completed" => AgentTaskState.Completed,
                _ => (AgentTaskState?)null,
            };
            var active = Str(todo, "activeForm");
            var text = ToolTargets.OneLine(state == AgentTaskState.InProgress && !string.IsNullOrWhiteSpace(active)
                ? active
                : Str(todo, "content"));
            if (state is not null && text.Length > 0)
            {
                list.Add(new AgentTask(text, state.Value));
            }
        }

        Todos = list;
    }

    /// <summary>Nombre de lignes d'un texte : la dernière fin de ligne ne compte pas pour une ligne de plus.</summary>
    private static int? CountLines(string? content)
    {
        if (content is null)
        {
            return null;
        }

        if (content.Length == 0)
        {
            return 0;
        }

        var lines = content.Count(c => c == '\n');
        return content[^1] == '\n' ? lines : lines + 1;
    }

    private static bool HasBlock(JsonElement content, string type) =>
        content.EnumerateArray().Any(block => block.ValueKind == JsonValueKind.Object && Str(block, "type") == type);

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static long Long(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : 0;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
