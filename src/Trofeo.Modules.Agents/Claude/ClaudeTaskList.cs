using System.Globalization;
using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>Lecture de la liste de tâches d'une session Claude Code (<c>&lt;claudeHome&gt;/tasks/&lt;sessionId&gt;/*.json</c>).</summary>
public static class ClaudeTaskList
{
    /// <summary>
    /// Les tâches de la session dans l'ordre de leur numéro (le nom du fichier), ou <c>null</c> si le dossier n'existe pas.
    /// Les tâches supprimées, de statut inconnu ou dans un fichier illisible sont ignorées.
    /// </summary>
    public static IReadOnlyList<AgentTask>? Read(string sessionTasksDir)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(sessionTasksDir))
            {
                return null;
            }

            files = Directory.GetFiles(sessionTasksDir, "*.json");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var ordered = files
            .Select(file => (File: file, Name: Path.GetFileNameWithoutExtension(file)))
            .Select(f => (f.File, f.Name, Number: long.TryParse(f.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (long?)null))
            .OrderBy(f => f.Number is null)
            .ThenBy(f => f.Number)
            .ThenBy(f => f.Name, StringComparer.Ordinal);

        var tasks = new List<AgentTask>();
        foreach (var (file, _, _) in ordered)
        {
            var task = ReadTask(file);
            if (task is not null)
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    private static AgentTask? ReadTask(string file)
    {
        try
        {
            // Claude Code peut être en train de réécrire le fichier : on le lit sans le verrouiller.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var state = Str(root, "status") switch
            {
                "pending" => AgentTaskState.Pending,
                "in_progress" => AgentTaskState.InProgress,
                "completed" => AgentTaskState.Completed,
                _ => (AgentTaskState?)null,
            };
            if (state is null)
            {
                return null;
            }

            var subject = Str(root, "subject");
            var active = Str(root, "activeForm");
            var text = ToolTargets.OneLine(state == AgentTaskState.InProgress && !string.IsNullOrWhiteSpace(active) ? active : subject);
            return text.Length == 0 ? null : new AgentTask(text, state.Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
