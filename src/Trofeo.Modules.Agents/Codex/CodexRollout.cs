using System.Globalization;
using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>
/// État d'une session Codex déduit de sa transcription (<c>sessions/AAAA/MM/JJ/rollout-*.jsonl</c>), ligne après ligne.
/// Le format est celui décrit par la communauté et n'est pas vérifié : un champ manquant ou d'un autre type est ignoré,
/// jamais une erreur. Pas de verrou : à utiliser depuis un seul thread.
/// </summary>
public sealed class CodexRollout
{
    private const int MaxFeed = 50;
    private const double FiveHourMaxMinutes = 300;
    private const double WeeklyMinMinutes = 10_080;
    private static readonly TimeSpan WorkingMaxSilence = TimeSpan.FromMinutes(10);

    private readonly List<FeedEntry> _feed = [];
    private IReadOnlyList<AgentTask> _tasks = [];
    // Une tâche a démarré (task_started) et rien ne l'a terminée depuis.
    private bool _taskOpen;
    // Un task_started, task_complete ou turn_aborted a été vu depuis le dernier Reset. Sinon (fin de fichier qui a coupé
    // le début d'un long tour), on ne sait pas si une tâche est ouverte et on se fie à la fraîcheur du dernier événement.
    private bool _turnSeen;
    // Le dernier événement est une demande d'approbation ou de réponse à l'utilisateur.
    private bool _waiting;

    /// <summary>L'identifiant de la session (<c>session_meta</c>).</summary>
    public string? SessionId { get; private set; }

    /// <summary>Le dossier de travail de la session.</summary>
    public string? Cwd { get; private set; }

    /// <summary>Le moment où la session a commencé.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>L'horodatage du dernier événement reconnu.</summary>
    public DateTimeOffset? LastEventAt { get; private set; }

    /// <summary>La dernière demande de l'utilisateur (sa première ligne non vide).</summary>
    public string? CurrentTask { get; private set; }

    /// <summary>Le moment de cette demande.</summary>
    public DateTimeOffset? TaskStartedAt { get; private set; }

    /// <summary>Les derniers outils utilisés, du plus ancien au plus récent, 50 au plus.</summary>
    public IReadOnlyList<FeedEntry> Feed => _feed;

    /// <summary>La dernière liste écrite par l'outil <c>update_plan</c> (vide s'il n'y en a pas eu).</summary>
    public IReadOnlyList<AgentTask> Tasks => _tasks;

    /// <summary>Le modèle du dernier <c>turn_context</c>.</summary>
    public string? Model { get; private set; }

    /// <summary>La part de la fenêtre de contexte remplie au dernier relevé de jetons, dans [0, 100].</summary>
    public double? ContextPercent { get; private set; }

    /// <summary>Les quotas du dernier relevé de jetons qui en portait.</summary>
    public AgentQuotas? Quotas { get; private set; }

    /// <summary>Oublie tout (le fichier a été remplacé, on le relit depuis la fin).</summary>
    public void Reset()
    {
        SessionId = null;
        Cwd = null;
        StartedAt = null;
        LastEventAt = null;
        CurrentTask = null;
        TaskStartedAt = null;
        _feed.Clear();
        _tasks = [];
        Model = null;
        ContextPercent = null;
        Quotas = null;
        _taskOpen = false;
        _turnSeen = false;
        _waiting = false;
    }

    /// <summary>
    /// Ce que fait la session à l'instant <paramref name="now"/> : elle attend l'utilisateur si une demande
    /// d'approbation est le dernier événement ; elle travaille si le dernier événement a moins de 10 minutes et que
    /// la tâche est ouverte (<c>task_started</c> sans fin depuis) ou que l'on n'a vu aucun début ni fin de tâche (le
    /// début d'un long tour a pu sortir de la partie lue) ; sinon elle est inactive.
    /// </summary>
    public AgentActivity Activity(DateTimeOffset now)
    {
        if (_waiting)
        {
            return AgentActivity.Waiting;
        }

        var open = !_turnSeen || _taskOpen;
        return open && LastEventAt is { } last && now - last < WorkingMaxSilence ? AgentActivity.Working : AgentActivity.Idle;
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
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var at = Time(Str(root, "timestamp"));
            var outcome = Str(root, "type") switch
            {
                "session_meta" => ApplyMeta(payload, at),
                "turn_context" => ApplyTurnContext(payload),
                "event_msg" => ApplyEvent(payload, at),
                "response_item" => ApplyItem(payload),
                _ => Outcome.Ignored,
            };

            if (outcome != Outcome.Ignored)
            {
                if (at is { } moment && (LastEventAt is not { } last || moment > last))
                {
                    LastEventAt = moment;
                }

                // Un relevé de jetons (ou la ligne de méta) ne répond pas à la demande : l'attente continue.
                if (outcome != Outcome.Passive)
                {
                    _waiting = outcome == Outcome.Approval;
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // Ligne tronquée ou abîmée (y compris une chaîne à substitut UTF-16 isolé, que GetString refuse) : on passe à la suivante.
        }
    }

    /// <summary>Effet d'une ligne : ignorée ; événement (met fin à l'attente) ; demande d'approbation ; ou événement qui laisse l'attente en l'état.</summary>
    private enum Outcome { Ignored, Event, Approval, Passive }

    private Outcome ApplyMeta(JsonElement payload, DateTimeOffset? at)
    {
        SessionId = Str(payload, "id") ?? SessionId;
        Cwd = Str(payload, "cwd") ?? Cwd;
        StartedAt = Time(Str(payload, "timestamp")) ?? at ?? StartedAt;
        return Outcome.Passive;
    }

    private Outcome ApplyTurnContext(JsonElement payload)
    {
        Model = Str(payload, "model") ?? Model;
        Cwd = Str(payload, "cwd") ?? Cwd;
        return Outcome.Event;
    }

    private Outcome ApplyEvent(JsonElement payload, DateTimeOffset? at)
    {
        switch (Str(payload, "type"))
        {
            case null:
                return Outcome.Ignored;
            case "user_message":
                {
                    var firstLine = (Str(payload, "message") ?? "").Split('\n').Select(line => line.Trim())
                        .FirstOrDefault(line => line.Length > 0);
                    if (firstLine is not null)
                    {
                        CurrentTask = ToolTargets.OneLine(firstLine);
                        TaskStartedAt = at;
                    }

                    break;
                }
            case "task_started":
                _taskOpen = true;
                _turnSeen = true;
                break;
            case "task_complete" or "turn_aborted":
                _taskOpen = false;
                _turnSeen = true;
                break;
            case "exec_approval_request" or "apply_patch_approval_request" or "request_user_input":
                return Outcome.Approval;
            case "token_count":
                ApplyTokenCount(payload, at);
                return Outcome.Passive;
        }

        return Outcome.Event;
    }

    private void ApplyTokenCount(JsonElement payload, DateTimeOffset? at)
    {
        // « info » est null avant le premier relevé : on garde alors la valeur précédente.
        if (Obj(payload, "info") is { } info && Obj(info, "last_token_usage") is { } last
            && Num(last, "input_tokens") is { } input && Num(info, "model_context_window") is { } window && window > 0)
        {
            ContextPercent = Math.Clamp(input * 100.0 / window, 0, 100);
        }

        if (at is { } captured && Obj(payload, "rate_limits") is { } limits)
        {
            ApplyQuotas(limits, captured);
        }
    }

    /// <summary>Range chaque fenêtre selon sa durée (≤ 300 min : 5 h ; ≥ 10 080 min : hebdo), à défaut selon son rang.</summary>
    private void ApplyQuotas(JsonElement limits, DateTimeOffset at)
    {
        QuotaWindow? fiveHour = null, weekly = null;
        foreach (var (name, defaultsToFiveHour) in new[] { ("primary", true), ("secondary", false) })
        {
            if (Obj(limits, name) is not { } window || Num(window, "used_percent") is not { } used)
            {
                continue;
            }

            bool? isFiveHour = Num(window, "window_minutes") switch
            {
                null => defaultsToFiveHour,
                <= FiveHourMaxMinutes => true,
                >= WeeklyMinMinutes => false,
                _ => null,
            };
            if (isFiveHour is null)
            {
                continue;
            }

            var quota = new QuotaWindow(used, ResetOf(window, at));
            if (isFiveHour.Value)
            {
                fiveHour ??= quota;
            }
            else
            {
                weekly ??= quota;
            }
        }

        // Fusion fenêtre par fenêtre : un relevé qui n'en porte qu'une garde l'autre telle qu'on la connaissait.
        if (fiveHour is not null || weekly is not null)
        {
            Quotas = new AgentQuotas(fiveHour ?? Quotas?.FiveHour, weekly ?? Quotas?.Weekly, at);
        }
    }

    /// <summary><c>resets_at</c> (secondes Unix), sinon <c>resets_in_seconds</c> ajouté à l'horodatage de la ligne.</summary>
    private static DateTimeOffset? ResetOf(JsonElement window, DateTimeOffset at)
    {
        if (Num(window, "resets_at") is { } unix && unix is > -62_135_596_800 and < 253_402_300_799)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)unix);
        }

        // Borné à 10 ans : au-delà, la valeur n'a pas de sens (et l'addition pourrait déborder).
        return Num(window, "resets_in_seconds") is { } seconds && seconds is >= 0 and < 315_360_000
            ? at.AddSeconds(seconds)
            : null;
    }

    private Outcome ApplyItem(JsonElement payload)
    {
        switch (Str(payload, "type"))
        {
            case null:
                return Outcome.Ignored;
            case "function_call":
                ApplyFunctionCall(payload);
                break;
            case "custom_tool_call":
                if (Str(payload, "name") == "apply_patch" && Str(payload, "input") is { } patch)
                {
                    AddPatch(patch);
                }

                break;
            case "web_search_call":
                AddFeed(new FeedEntry("Recherche", ToolTargets.OneLine(Str(Obj(payload, "action"), "query"))));
                break;
        }

        return Outcome.Event;
    }

    private void ApplyFunctionCall(JsonElement payload)
    {
        var name = Str(payload, "name");
        switch (name)
        {
            case null:
                return;
            case "shell" or "exec_command" or "local_shell":
                {
                    var args = Arguments(payload);
                    AddFeed(new FeedEntry("Shell", ToolTargets.OneLine(args is { } a ? ShellCommand(a) : null)));
                    return;
                }
            case "apply_patch":
                {
                    // Les arguments sont un objet JSON portant le patch, ou le patch lui-même.
                    var args = Arguments(payload);
                    var text = args is { } a ? Str(a, "input") ?? Str(a, "patch") : Str(payload, "arguments");
                    if (text is not null)
                    {
                        AddPatch(text);
                    }

                    return;
                }
            case "update_plan":
                if (Arguments(payload) is { } plan)
                {
                    ApplyPlan(plan);
                }

                return;
            default:
                AddFeed(new FeedEntry(ToolTargets.OneLine(name), ""));
                return;
        }
    }

    private void ApplyPlan(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("plan", out var plan) || plan.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var list = new List<AgentTask>();
        foreach (var item in plan.EnumerateArray())
        {
            var state = Str(item, "status") switch
            {
                "pending" => AgentTaskState.Pending,
                "in_progress" => AgentTaskState.InProgress,
                "completed" => AgentTaskState.Completed,
                _ => (AgentTaskState?)null,
            };
            var text = ToolTargets.OneLine(Str(item, "step"));
            if (state is not null && text.Length > 0)
            {
                list.Add(new AgentTask(text, state.Value));
            }
        }

        _tasks = list;
    }

    /// <summary>Les arguments d'un appel de fonction : un texte JSON (cas normal) ou déjà un objet ; <c>null</c> sinon.</summary>
    private static JsonElement? Arguments(JsonElement payload)
    {
        if (!payload.TryGetProperty("arguments", out var arguments))
        {
            return null;
        }

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            return arguments;
        }

        if (arguments.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            using var parsed = JsonDocument.Parse(arguments.GetString()!);
            return parsed.RootElement.ValueKind == JsonValueKind.Object ? parsed.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>La commande : <c>command</c> (ou <c>cmd</c>) en tableau, joint par des espaces, ou en texte.</summary>
    private static string? ShellCommand(JsonElement args)
    {
        foreach (var key in new[] { "command", "cmd" })
        {
            if (!args.TryGetProperty(key, out var command))
            {
                continue;
            }

            if (command.ValueKind == JsonValueKind.String)
            {
                return command.GetString();
            }

            if (command.ValueKind == JsonValueKind.Array)
            {
                return string.Join(' ', command.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.String).Select(part => part.GetString()));
            }
        }

        return null;
    }

    /// <summary>Ajoute une entrée « Patch » : le premier fichier touché, les lignes ajoutées et retirées (hors en-têtes <c>***</c>).</summary>
    private void AddPatch(string patch)
    {
        string? path = null;
        int added = 0, removed = 0;
        foreach (var raw in patch.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("***", StringComparison.Ordinal))
            {
                path ??= PatchPath(line);
            }
            else if (line.StartsWith('+'))
            {
                added++;
            }
            else if (line.StartsWith('-'))
            {
                removed++;
            }
        }

        AddFeed(new FeedEntry("Patch", ToolTargets.OneLine(path), added, removed));
    }

    private static string? PatchPath(string header)
    {
        foreach (var prefix in new[] { "*** Update File: ", "*** Add File: ", "*** Delete File: " })
        {
            if (header.StartsWith(prefix, StringComparison.Ordinal))
            {
                return header[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private void AddFeed(FeedEntry entry)
    {
        _feed.Add(entry);
        if (_feed.Count > MaxFeed)
        {
            _feed.RemoveAt(0);
        }
    }

    private static DateTimeOffset? Time(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;

    private static JsonElement? Obj(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static double? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : null;

    private static string? Str(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
