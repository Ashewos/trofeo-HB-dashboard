using System.Text.Json;

namespace Trofeo.Modules.Agents;

/// <summary>Index local facultatif des titres Desktop. Ni base de données ni identifiants d'authentification lus.</summary>
internal sealed class CodexTitles(string home)
{
    private readonly string _path = Path.Combine(home, "session_index.jsonl");
    private Dictionary<string, string> _titles = new(StringComparer.Ordinal);
    private (long Length, DateTime Written)? _version;

    public string? Find(string id) => _titles.GetValueOrDefault(id);

    public void Refresh()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                _titles.Clear();
                _version = null;
                return;
            }

            var version = (info.Length, info.LastWriteTimeUtc);
            if (_version == version || info.Length > 8 * 1024 * 1024)
            {
                return;
            }

            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object
                        && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        && root.TryGetProperty("thread_name", out var name) && name.ValueKind == JsonValueKind.String
                        && id.GetString() is { Length: > 0 } key && name.GetString() is { } text
                        && !string.IsNullOrWhiteSpace(text))
                    {
                        titles[key] = ToolTargets.OneLine(text);
                    }
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException)
                {
                    // Une ligne en cours d'écriture ou inattendue ne masque pas les autres titres.
                }
            }

            _titles = titles;
            _version = version;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Index absent ou verrouillé : repli sur la demande ou le dossier, puis nouvelle tentative.
        }
    }
}
