using System.Globalization;
using System.Text.Json;

namespace Trofeo.StatusLine;

/// <summary>Un quota (fenêtre de 5 h ou hebdomadaire) tel que Claude Code le transmet.</summary>
public sealed record CapturedQuota(double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>Ce que l'on garde d'une mise à jour de la barre d'état : une session, son modèle, son contexte, ses quotas.</summary>
public sealed record StatusLineRecord(
    DateTimeOffset CapturedAt,
    string SessionId,
    string? ModelId,
    long? ContextWindowSize,
    double? ContextUsedPercent,
    CapturedQuota? FiveHour,
    CapturedQuota? SevenDay);

/// <summary>Lecture du JSON que Claude Code envoie à la commande de barre d'état.</summary>
public static class StatusLineCapture
{
    /// <summary>
    /// Lit le JSON transmis par Claude Code ; <c>null</c> s'il est illisible ou sans <c>session_id</c> valide.
    /// Un champ absent ou d'un autre type vaut <c>null</c>, sans erreur.
    /// </summary>
    public static StatusLineRecord? Parse(ReadOnlySpan<byte> json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var sessionId = Child(root, "session_id") is { ValueKind: JsonValueKind.String } id ? id.GetString() : null;
            if (!IsValidSessionId(sessionId)) return null;

            var model = Child(root, "model");
            var context = Child(root, "context_window");
            var limits = Child(root, "rate_limits");

            return new StatusLineRecord(
                now,
                sessionId!,
                model is { } m && Child(m, "id") is { ValueKind: JsonValueKind.String } modelId ? modelId.GetString() : null,
                context is { } c ? ToLong(Child(c, "context_window_size")) : null,
                context is { } c2 ? ToDouble(Child(c2, "used_percentage")) : null,
                limits is { } l ? ParseQuota(Child(l, "five_hour")) : null,
                limits is { } l2 ? ParseQuota(Child(l2, "seven_day")) : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Identifiant de session sûr comme nom de fichier : <c>[A-Za-z0-9-]</c>, 1 à 64 caractères.</summary>
    internal static bool IsValidSessionId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (var ch in id)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '-') return false;
        }
        return true;
    }

    private static JsonElement? Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : null;

    private static double? ToDouble(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } e && e.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    private static long? ToLong(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Number } e) return null;
        if (e.TryGetInt64(out var l)) return l;
        return ToDouble(e) is { } d && Math.Abs(d) < 9e18 ? (long)Math.Round(d) : null;
    }

    private static CapturedQuota? ParseQuota(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        if (ToDouble(Child(e, "used_percentage")) is not { } used) return null;
        return new CapturedQuota(used, ParseResetsAt(Child(e, "resets_at")));
    }

    /// <summary>Secondes Unix (nombre) ou date ISO 8601 (chaîne).</summary>
    private static DateTimeOffset? ParseResetsAt(JsonElement? element)
    {
        try
        {
            switch (element)
            {
                case { ValueKind: JsonValueKind.Number } n when ToDouble(n) is { } seconds:
                    return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
                case { ValueKind: JsonValueKind.String } s
                    when DateTimeOffset.TryParse(s.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date):
                    return date;
                default:
                    return null;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // secondes hors de l'intervalle des dates
        }
    }
}
