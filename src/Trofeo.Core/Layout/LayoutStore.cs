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

    /// <summary>
    /// Ne lève jamais d'exception pour un problème de fichier. Fichier absent ou illisible (verrouillé) : disposition
    /// par défaut, sans copie. Fichier corrompu ou invalide (JSON illisible, rotation hors {0, 180}, module hors du
    /// damier, modules qui se chevauchent, type absent, identifiants d'instance en double) : défaut, et copie conservée en <c>.bad</c>.
    /// Les types inconnus et les tailles minimum dépendent du registre des modules : à vérifier par l'appelant
    /// avec <see cref="LayoutValidator.Check"/>.
    /// </summary>
    public DashboardLayout Load()
    {
        string json;
        try
        {
            if (!File.Exists(_filePath)) return DashboardLayout.Default;
            json = File.ReadAllText(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DashboardLayout.Default;
        }

        try
        {
            var layout = JsonSerializer.Deserialize<DashboardLayout>(json, Options);
            if (layout is null || !IsValid(layout))
                throw new JsonException("Disposition invalide.");
            return layout;
        }
        catch (Exception)
        {
            // Toute erreur de lecture ou de validation (JSON, type absent, débordement...) : défaut + copie .bad.
            try
            {
                File.Copy(_filePath, _filePath + ".bad", overwrite: true);
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                // La sauvegarde est un bonus : on retombe quand même sur la disposition par défaut.
            }
            return DashboardLayout.Default;
        }
    }

    private static bool IsValid(DashboardLayout layout)
    {
        if (layout.Rotation is not (0 or 180) || layout.Placements is null) return false;

        if (layout.Shortcuts is { } shortcuts && shortcuts.Any(s => s is null)) return false;

        var placements = layout.Placements;
        if (placements.Any(p => p is null || string.IsNullOrWhiteSpace(p.TypeId) ||
                                p.Settings.ValueKind == JsonValueKind.Undefined || !LayoutValidator.IsInBounds(p)))
            return false;

        if (placements.Select(p => p.InstanceId).Distinct().Count() != placements.Count) return false;

        for (var i = 0; i < placements.Count; i++)
            for (var j = i + 1; j < placements.Count; j++)
                if (LayoutValidator.Intersects(placements[i], placements[j])) return false;

        return true;
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
