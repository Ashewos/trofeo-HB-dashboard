using System.Globalization;
using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>
/// État d'une session Codex déduit de sa transcription (<c>sessions/AAAA/MM/JJ/rollout-*.jsonl</c>), ligne après ligne.
/// Accepte les événements historiques et les messages de Codex Desktop : un champ manquant ou d'un autre type est ignoré,
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
    // Moment du dernier événement d'activité (response_item, ou event_msg autre que token_count) : session_meta,
    // turn_context et token_count ne disent pas que l'agent travaille.
    private DateTimeOffset? _lastWorkAt;
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

    /// <summary>Première demande lisible, repli du titre quand l'index des conversations est absent.</summary>
    public string? Title { get; private set; }

    /// <summary>Les sessions de sous-agents internes ne sont pas des conversations utilisateur.</summary>
    public bool IsInternal { get; private set; }

    /// <summary>Le moment de cette demande.</summary>
    public DateTimeOffset? TaskStartedAt { get; private set; }

    /// <summary>Fin du dernier traitement, pour figer la durée tant qu'aucun nouveau tour ne démarre.</summary>
    public DateTimeOffset? TaskFinishedAt { get; private set; }

    private string? _lastUserMessage;
    private DateTimeOffset? _lastUserAt;
    private bool _lastUserShown;

    /// <summary>Les derniers outils utilisés, du plus ancien au plus récent, 50 au plus.</summary>
    public IReadOnlyList<FeedEntry> Feed => _feed;

    /// <summary>La dernière liste écrite par l'outil <c>update_plan</c> (vide s'il n'y en a pas eu).</summary>
    public IReadOnlyList<AgentTask> Tasks => _tasks;

    /// <summary>Le modèle du dernier <c>turn_context</c>.</summary>
    public string? Model { get; private set; }

    /// <summary>Récupération historique au démarrage, sans remplacer le modèle d'un événement plus récent.</summary>
    internal void RestoreModel(string? model) => Model ??= model;

    /// <summary>Remplit un fil vide depuis le même fichier, sans rejouer l'activité, la durée ou les quotas.</summary>
    internal void RestoreConversation(CodexRollout? history)
    {
        if (history is null || _feed.Count != 0) { return; }
        foreach (var entry in history.Feed) { AddFeed(entry); }
        CurrentTask ??= history.CurrentTask;
        Title ??= history.Title;
    }

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
        Title = null;
        IsInternal = false;
        TaskStartedAt = null;
        TaskFinishedAt = null;
        _lastUserMessage = null;
        _lastUserAt = null;
        _lastUserShown = false;
        _feed.Clear();
        _tasks = [];
        Model = null;
        ContextPercent = null;
        Quotas = null;
        _taskOpen = false;
        _turnSeen = false;
        _lastWorkAt = null;
        _waiting = false;
    }

    /// <summary>
    /// Ce que fait la session à l'instant <paramref name="now"/> : elle attend l'utilisateur si une demande
    /// d'approbation est le dernier événement ; elle travaille si le dernier événement a moins de 10 minutes et que
    /// la tâche est ouverte (<c>task_started</c> sans fin depuis) ; sans aucun début ni fin de tâche vus (le début d'un
    /// long tour a pu sortir de la partie lue), c'est un événement d'activité de moins de 10 minutes qui compte ;
    /// sinon elle est inactive.
    /// </summary>
    public AgentActivity Activity(DateTimeOffset now)
    {
        if (_waiting)
        {
            return AgentActivity.Waiting;
        }

        var last = _turnSeen ? (_taskOpen ? LastEventAt : null) : _lastWorkAt;
        return last is { } moment && now - moment < WorkingMaxSilence ? AgentActivity.Working : AgentActivity.Idle;
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
            var type = Str(root, "type");
            var outcome = type switch
            {
                "session_meta" => ApplyMeta(payload, at),
                "turn_context" => ApplyTurnContext(payload),
                "event_msg" => ApplyEvent(payload, at),
                "response_item" => ApplyItem(payload, at),
                _ => Outcome.Ignored,
            };

            if (outcome != Outcome.Ignored)
            {
                if (at is { } moment && (LastEventAt is not { } last || moment > last))
                {
                    LastEventAt = moment;
                }

                if (at is { } worked && type is ("response_item" or "event_msg") && outcome != Outcome.Passive
                    && (_lastWorkAt is not { } lastWork || worked > lastWork))
                {
                    _lastWorkAt = worked;
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
        IsInternal = Obj(payload, "source") is { } source && source.TryGetProperty("subagent", out _);
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
                ApplyUserMessage(Str(payload, "message"), at, showInFeed: false);
                break;
            case "task_started":
                TaskStartedAt = EventTime(payload, "started_at") ?? (TaskFinishedAt is not null ? at : TaskStartedAt ?? at);
                TaskFinishedAt = null;
                _taskOpen = true;
                _turnSeen = true;
                break;
            case "task_complete" or "turn_aborted":
                TaskStartedAt = EventTime(payload, "started_at") ?? TaskStartedAt;
                TaskFinishedAt = EventTime(payload, "completed_at") ?? at;
                if (TaskFinishedAt is { } end && Num(payload, "duration_ms") is { } duration
                    && duration is >= 0 and <= 315_360_000_000 && duration <= (end - DateTimeOffset.MinValue).TotalMilliseconds)
                {
                    // Desktop fournit aussi la durée exacte, même si le début du tour est hors de la partie relue.
                    TaskStartedAt = end - TimeSpan.FromMilliseconds(duration);
                }
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
            && Num(info, "model_context_window") is { } window && window > 0)
        {
            // Après compression, input_tokens peut être nul alors que total_tokens estime le contexte conservé.
            // Ne jamais utiliser info.total_token_usage : c'est le cumul de toute la conversation.
            var used = Num(last, "total_tokens") is >= 0 and var total ? total : Num(last, "input_tokens");
            if (used is { } tokens)
            {
                ContextPercent = Math.Clamp(tokens * 100.0 / window, 0, 100);
            }
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

    private Outcome ApplyItem(JsonElement payload, DateTimeOffset? at)
    {
        switch (Str(payload, "type"))
        {
            case null:
                return Outcome.Ignored;
            case "message":
                return ApplyMessage(payload, at);
            case "function_call":
                ApplyFunctionCall(payload);
                break;
            case "custom_tool_call":
                var customName = Str(payload, "name");
                if (customName is "apply_patch" or "functions.apply_patch" && Str(payload, "input") is { } patch)
                {
                    AddPatch(patch);
                }
                else if (customName is "exec" or "functions.exec" && Str(payload, "input") is { } script)
                {
                    ApplyToolScript(script);
                }

                break;
            case "web_search_call":
                AddFeed(new FeedEntry("Recherche", ToolTargets.OneLine(Str(Obj(payload, "action"), "query"))));
                break;
        }

        return Outcome.Event;
    }

    private Outcome ApplyMessage(JsonElement payload, DateTimeOffset? at)
    {
        var role = Str(payload, "role");
        if (role is not ("user" or "assistant") || !payload.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return Outcome.Passive;
        }

        var text = string.Join('\n', content.EnumerateArray()
            .Where(part => Str(part, "type") is "input_text" or "output_text" or "text")
            .Select(part => Str(part, "text")).Where(part => !string.IsNullOrWhiteSpace(part)));
        if (role == "user")
        {
            return ApplyUserMessage(text, at, showInFeed: true) ? Outcome.Event : Outcome.Passive;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            AddFeed(new FeedEntry("Codex", ToolTargets.OneLine(text)));
        }

        return Outcome.Event;
    }

    private bool ApplyUserMessage(string? text, DateTimeOffset? at, bool showInFeed)
    {
        var prompt = UserPrompt(text);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        // Certaines versions écrivent à la fois event_msg/user_message et response_item/message.
        var duplicate = _lastUserMessage == prompt && at is { } moment && _lastUserAt is { } previous
            && (moment - previous).Duration() < TimeSpan.FromSeconds(1);
        if (duplicate)
        {
            if (showInFeed && !_lastUserShown)
            {
                AddFeed(new FeedEntry("Vous", ToolTargets.OneLine(prompt)));
                _lastUserShown = true;
            }

            return false;
        }

        _lastUserMessage = prompt;
        _lastUserAt = at;
        _lastUserShown = showInFeed;
        CurrentTask = ToolTargets.OneLine(prompt.Split('\n').First(line => !string.IsNullOrWhiteSpace(line)));
        Title ??= CurrentTask;
        if (!_taskOpen || TaskStartedAt is null)
        {
            TaskStartedAt = at;
        }

        TaskFinishedAt = null;
        if (showInFeed)
        {
            AddFeed(new FeedEntry("Vous", ToolTargets.OneLine(prompt)));
        }

        return true;
    }

    /// <summary>Écarte le contexte injecté dans un message utilisateur sans écarter la demande qui le suit.</summary>
    private static string? UserPrompt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        const string request = "## My request:";
        var requestAt = text.IndexOf(request, StringComparison.Ordinal);
        if (requestAt >= 0)
        {
            return text[(requestAt + request.Length)..].Trim();
        }

        foreach (var tag in new[] { "environment_context", "permissions instructions", "collaboration_mode", "in-app-browser-context" })
        {
            if (text.StartsWith("<" + tag, StringComparison.Ordinal))
            {
                var closing = "</" + tag + ">";
                var end = text.IndexOf(closing, StringComparison.Ordinal);
                return end < 0 ? null : UserPrompt(text[(end + closing.Length)..]);
            }
        }

        if (text.StartsWith("# AGENTS.md instructions", StringComparison.Ordinal)
            || text.StartsWith("<INSTRUCTIONS>", StringComparison.Ordinal))
        {
            return null;
        }

        return text;
    }

    private static DateTimeOffset? EventTime(JsonElement payload, string name)
    {
        if (Num(payload, name) is { } unix && unix is > -62_135_596_800 and < 253_402_300_799)
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)unix);
        }

        return Time(Str(payload, name));
    }

    private void ApplyFunctionCall(JsonElement payload)
    {
        var name = Str(payload, "name");
        if (name?.StartsWith("functions.", StringComparison.Ordinal) == true)
        {
            name = name["functions.".Length..];
        }
        switch (name)
        {
            case null:
                return;
            case "shell" or "exec_command" or "local_shell":
                {
                    var args = Arguments(payload);
                    var command = args is { } a ? ShellCommand(a) : null;
                    AddFeed(new FeedEntry(ShellLabel(args is { } obj ? Str(obj, "shell") : null, command), ToolTargets.OneLine(command)));
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

    private void ApplyToolScript(string script)
    {
        foreach (var call in CodexToolScript.Read(script))
        {
            switch (call.Name)
            {
                case "exec_command":
                    AddFeed(new FeedEntry(ShellLabel(call.Shell, call.Text, usePlatformDefault: true), ToolTargets.OneLine(call.Text ?? "Commande dynamique")));
                    break;
                case "apply_patch":
                    if (!string.IsNullOrEmpty(call.Text)) { AddPatch(call.Text); }
                    break;
                case "write_stdin":
                    // C'est le suivi d'une commande déjà affichée, pas une nouvelle commande.
                    break;
                default:
                    AddFeed(new FeedEntry(ToolTargets.OneLine(call.Name), ToolTargets.OneLine(call.Text)));
                    break;
            }
        }
    }

    private static string ShellLabel(string? shell, string? command, bool usePlatformDefault = false)
    {
        var executable = shell?.Replace('\\', '/').Split('/').Last().ToLowerInvariant();
        if (executable is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe"
            || command?.TrimStart().StartsWith("powershell", StringComparison.OrdinalIgnoreCase) == true
            || command?.TrimStart().StartsWith("pwsh", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "PowerShell";
        }

        if (executable is "bash" or "bash.exe") { return "Bash"; }
        if (executable is "cmd" or "cmd.exe") { return "CMD"; }
        return usePlatformDefault && shell is null && OperatingSystem.IsWindows() ? "PowerShell" : "Shell";
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

        // Les chemins absolus Desktop commencent tous par le même long dossier utilisateur : montrer le fichier utile.
        var displayPath = path?.Replace('\\', '/');
        if (displayPath is not null && (displayPath.StartsWith('/') || (displayPath.Length > 2 && displayPath[1] == ':')))
        {
            displayPath = displayPath.Split('/').Last();
        }

        AddFeed(new FeedEntry("Patch", ToolTargets.OneLine(displayPath), added, removed));
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
