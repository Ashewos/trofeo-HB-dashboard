using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trofeo.Core.Layout;

/// <summary>Lecture et écriture de la disposition dans un fichier JSON.</summary>
public sealed class LayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public LayoutStore(string filePath) => _filePath = filePath;

    /// <summary>Fichier absent : disposition par défaut. Fichier corrompu : défaut, et copie conservée en <c>.bad</c>.</summary>
    public DashboardLayout Load()
    {
        if (!File.Exists(_filePath)) return DashboardLayout.Default;

        try
        {
            var layout = JsonSerializer.Deserialize<DashboardLayout>(File.ReadAllText(_filePath), Options);
            if (layout?.Placements is null || layout.Placements.Any(p => p is null || p.Settings.ValueKind == JsonValueKind.Undefined))
                throw new JsonException("Disposition incomplète.");
            return layout;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            File.Copy(_filePath, _filePath + ".bad", overwrite: true);
            return DashboardLayout.Default;
        }
    }

    /// <summary>Écriture atomique : fichier temporaire puis remplacement.</summary>
    public void Save(DashboardLayout layout)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(layout, Options));
        File.Move(tmp, _filePath, overwrite: true);
    }
}
