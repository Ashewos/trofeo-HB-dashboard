using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>Retrouve le dernier modèle connu quand le début d'un long tour n'est plus dans la portion relue.</summary>
internal static class CodexModelHistory
{
    // Une seule récupération par initialisation/troncature, en lecture séquentielle et mémoire bornée.
    private const int MaxBytes = 16 * 1024 * 1024;

    public static string? ReadLatest(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - MaxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            if (start > 0) { reader.ReadLine(); } // première ligne potentiellement coupée
            string? model = null;
            while (reader.ReadLine() is { } line)
            {
                if (line.Length > 1024 * 1024 || !line.Contains("\"turn_context\"", StringComparison.Ordinal)) { continue; }
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "turn_context"
                        && root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
                        && payload.TryGetProperty("model", out var value) && value.ValueKind == JsonValueKind.String
                        && value.GetString() is { } name && !string.IsNullOrWhiteSpace(name))
                    {
                        model = name;
                    }
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException)
                {
                    // Ligne partielle/inattendue : le dernier modèle valide reste utilisable.
                }
            }

            return model;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
