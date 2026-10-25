using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trofeo.Core.Rendering;

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
    /// <c>Appearance</c> est lu à part, avec indulgence : s'il est illisible (mauvais type JSON, <c>Kind</c> inconnu), il
    /// vaut <c>null</c> et la disposition est gardée, sans <c>.bad</c>. Un thème inconnu est gardé tel quel (c'est
    /// <see cref="AppearanceResolver"/> qui retombe sur Classique).
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
            var layout = Deserialize(json);
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

    private static DashboardLayout? Deserialize(string json)
    {
        // Si la racine n'est pas un objet, la désérialisation normale décide (et échoue comme avant).
        if (JsonNode.Parse(json) is not JsonObject root || !root.TryGetPropertyValue(nameof(DashboardLayout.Appearance), out var node))
            return JsonSerializer.Deserialize<DashboardLayout>(json, Options);

        root.Remove(nameof(DashboardLayout.Appearance));
        var layout = root.Deserialize<DashboardLayout>(Options);
        return layout is null ? null : layout with { Appearance = ReadAppearance(node) };
    }

    private static AppearanceSettings? ReadAppearance(JsonNode? node)
    {
        try
        {
            var appearance = node.Deserialize<AppearanceSettings>(Options);
            if (appearance is null || appearance.Theme is null) return null;
            if (appearance.Background is { } background && !Enum.IsDefined(background.Kind)) return null;
            return appearance;
        }
        catch (Exception)
        {
            // Mauvais type JSON, Kind inconnu... : pas d'apparence, mais la disposition reste lue.
            return null;
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
