using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>Nom affiché et cible d'un appel d'outil de Claude Code, pour une ligne du fil d'activité.</summary>
public static class ToolTargets
{
    /// <summary>Longueur maximale d'une cible ou d'une tâche, en caractères.</summary>
    internal const int MaxLength = 200;

    /// <summary>
    /// Ce qu'on affiche pour un appel d'outil, ou <c>null</c> quand l'appel n'a pas sa place dans le fil
    /// (outils de gestion des tâches, recherche d'outils).
    /// </summary>
    public static (string Tool, string Target)? Describe(string name, JsonElement input)
    {
        switch (name)
        {
            case "Read" or "Write" or "Edit":
                return (name, FileName(Str(input, "file_path")));
            case "MultiEdit":
                return ("Edit", FileName(Str(input, "file_path")));
            case "NotebookEdit":
                return ("Edit", FileName(Str(input, "notebook_path")));
            case "Bash":
                {
                    var description = Str(input, "description");
                    return ("Bash", OneLine(string.IsNullOrWhiteSpace(description) ? FirstLine(Str(input, "command")) : description));
                }
            case "Grep" or "Glob":
                return (name, OneLine(Str(input, "pattern")));
            case "WebFetch":
                return ("Web", Host(Str(input, "url")));
            case "WebSearch":
                return ("Recherche", OneLine(Str(input, "query")));
            case "Task" or "Agent":
                return ("Agent", OneLine(Str(input, "description")));
            case "Skill":
                return ("Skill", OneLine(Str(input, "skill")));
            case "TodoWrite" or "TaskCreate" or "TaskUpdate" or "TaskList" or "TaskGet" or "ToolSearch":
                return null;
        }

        // mcp__<serveur>__<outil> : on affiche l'outil seul.
        if (name.StartsWith("mcp__", StringComparison.Ordinal))
        {
            var separator = name.IndexOf("__", "mcp__".Length, StringComparison.Ordinal);
            if (separator >= 0 && separator + 2 < name.Length)
            {
                return (name[(separator + 2)..], FirstText(input));
            }
        }

        return (name, FirstText(input));
    }

    /// <summary>Une seule ligne de 200 caractères au plus : retours à la ligne remplacés par des espaces.</summary>
    internal static string OneLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var line = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ').Trim();
        if (line.Length <= MaxLength)
        {
            return line;
        }

        // On ne coupe pas une paire de substitution (emoji) en deux.
        var length = char.IsHighSurrogate(line[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return line[..length];
    }

    private static string FirstLine(string? text) =>
        (text ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";

    private static string FileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        var cut = path.LastIndexOfAny(['\\', '/']);
        return OneLine(cut < 0 ? path : path[(cut + 1)..]);
    }

    private static string Host(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : OneLine(url);
    }

    private static string FirstText(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (var property in input.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return OneLine(property.Value.GetString());
            }
        }

        return "";
    }

    private static string? Str(JsonElement input, string property) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
