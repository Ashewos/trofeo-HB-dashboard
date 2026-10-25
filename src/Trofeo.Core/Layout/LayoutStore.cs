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
    private readonly Action<string>? _warn;

    /// <param name="filePath">Le fichier <c>layout.json</c>.</param>
    /// <param name="warn">Reçoit les avertissements (en français) : apparence illisible abandonnée.</param>
    public LayoutStore(string filePath, Action<string>? warn = null)
    {
        _filePath = filePath;
        _warn = warn;
    }

    /// <summary>Avertissement quand une <c>Appearance</c> illisible est abandonnée.</summary>
    internal const string AppearanceDropped =
        "Réglages d'apparence illisibles dans layout.json : apparence par défaut utilisée.";

    /// <summary>
    /// Ne lève jamais d'exception pour un problème de fichier. Fichier absent ou illisible (verrouillé) : disposition
    /// par défaut, sans copie. Fichier corrompu ou invalide (JSON illisible, rotation hors {0, 180}, module hors du
    /// damier, modules qui se chevauchent, type absent, identifiants d'instance en double) : défaut, et copie conservée en <c>.bad</c>.
    /// <c>Appearance</c> est lu à part, avec indulgence : s'il est illisible (mauvais type JSON, <c>Kind</c> inconnu), il
    /// vaut <c>null</c> et la disposition est gardée, sans <c>.bad</c>, avec un avertissement au <c>warn</c> du constructeur.
    /// Un thème inconnu est gardé tel quel (c'est
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

    private DashboardLayout? Deserialize(string json)
    {
        JsonObject? root;
        JsonNode? node = null;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
            // Lever ici (clé en double) vaut ArgumentException : voir plus bas.
            if (root is null || !root.TryGetPropertyValue(nameof(DashboardLayout.Appearance), out node))
                return JsonSerializer.Deserialize<DashboardLayout>(json, Options); // racine non-objet, ou pas d'Appearance
        }
        catch (ArgumentException)
        {
            // Un nom de propriété en double : JsonNode refuse, le sérialiseur l'acceptait (la dernière valeur gagne) et
            // les fichiers d'avant le plan 4 doivent rester lus. Chemin d'avant, sans lecture indulgente de l'Appearance.
            return JsonSerializer.Deserialize<DashboardLayout>(json, Options);
        }

        root.Remove(nameof(DashboardLayout.Appearance));
        var layout = root.Deserialize<DashboardLayout>(Options);
        return layout is null ? null : layout with { Appearance = ReadAppearance(node) };
    }

    private AppearanceSettings? ReadAppearance(JsonNode? node)
    {
        if (node is null) return null; // "Appearance": null, voulu

        try
        {
            var appearance = node.Deserialize<AppearanceSettings>(Options);
            if (appearance is not null && appearance.Theme is not null &&
                (appearance.Background is not { } background || Enum.IsDefined(background.Kind)))
                return appearance;
        }
        catch (Exception)
        {
            // Mauvais type JSON, Kind inconnu... : pas d'apparence, mais la disposition reste lue.
        }

        _warn?.Invoke(AppearanceDropped);
        return null;
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
