using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Trofeo.StatusLine;

/// <summary>Fichiers de capture : un JSON par session, <c>&lt;dossier&gt;/&lt;sessionId&gt;.json</c>, en camelCase.</summary>
public static class StatusLineFile
{
    /// <summary>Écrit la capture de façon atomique (fichier temporaire <c>.tmp</c> puis déplacement).</summary>
    public static void Write(string dir, StatusLineRecord record)
    {
        if (!StatusLineCapture.IsValidSessionId(record.SessionId))
            throw new ArgumentException("Identifiant de session invalide.", nameof(record));

        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, record.SessionId + ".json");
        // Un fichier temporaire par processus : deux lancements simultanés ne s'écrasent pas.
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllBytes(temp, Serialize(record));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Lit les captures récentes, de la plus récente à la plus ancienne ; ignore les fichiers illisibles et ceux plus vieux que <paramref name="maxAge"/>.</summary>
    public static IReadOnlyList<StatusLineRecord> ReadAll(string dir, DateTimeOffset now, TimeSpan maxAge)
    {
        var records = new List<StatusLineRecord>();
        if (!Directory.Exists(dir)) return records;

        foreach (var path in EnumerateCaptures(dir))
        {
            var record = TryRead(path);
            if (record is not null && now - record.CapturedAt <= maxAge) records.Add(record);
        }
        records.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));
        return records;
    }

    /// <summary>Supprime les captures plus vieilles que <paramref name="age"/> (pour un fichier illisible, la date de dernière écriture fait foi).</summary>
    public static void DeleteOlderThan(string dir, DateTimeOffset now, TimeSpan age)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var path in EnumerateCaptures(dir))
        {
            try
            {
                var when = TryRead(path)?.CapturedAt ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (now - when > age) File.Delete(path);
            }
            catch (IOException)
            {
                // Fichier en cours d'utilisation : on réessaiera plus tard.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static IEnumerable<string> EnumerateCaptures(string dir) =>
        Directory.EnumerateFiles(dir, "*.json").Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private static byte[] Serialize(StatusLineRecord record)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("capturedAt", record.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("sessionId", record.SessionId);
            WriteString(w, "modelId", record.ModelId);
            if (record.ContextWindowSize is { } size) w.WriteNumber("contextWindowSize", size); else w.WriteNull("contextWindowSize");
            if (record.ContextUsedPercent is { } pct) w.WriteNumber("contextUsedPercent", pct); else w.WriteNull("contextUsedPercent");
            WriteQuota(w, "fiveHour", record.FiveHour);
            WriteQuota(w, "sevenDay", record.SevenDay);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name); else w.WriteString(name, value);
    }

    private static void WriteQuota(Utf8JsonWriter w, string name, CapturedQuota? quota)
    {
        if (quota is null) { w.WriteNull(name); return; }
        w.WriteStartObject(name);
        w.WriteNumber("usedPercent", quota.UsedPercent);
        if (quota.ResetsAt is { } at) w.WriteString("resetsAt", at.ToString("O", CultureInfo.InvariantCulture)); else w.WriteNull("resetsAt");
        w.WriteEndObject();
    }

    private static StatusLineRecord? TryRead(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Date(root, "capturedAt") is not { } capturedAt) return null;
            var sessionId = Text(root, "sessionId");
            if (!StatusLineCapture.IsValidSessionId(sessionId)) return null;

            return new StatusLineRecord(
                capturedAt,
                sessionId!,
                Text(root, "modelId"),
                Number(root, "contextWindowSize") is { } size ? (long)size : null,
                Number(root, "contextUsedPercent"),
                Quota(root, "fiveHour"),
                Quota(root, "sevenDay"));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CapturedQuota? Quota(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var q) || q.ValueKind != JsonValueKind.Object) return null;
        return Number(q, "usedPercent") is { } used ? new CapturedQuota(used, Date(q, "resetsAt")) : null;
    }

    private static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Number(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static DateTimeOffset? Date(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : null;
}
