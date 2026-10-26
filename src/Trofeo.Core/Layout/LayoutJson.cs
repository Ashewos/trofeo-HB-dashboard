using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trofeo.Core.Rendering;
using Trofeo.Core.Shortcuts;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Layout;

/// <summary>
/// Résultat de la lecture d'une disposition : soit <see cref="Layout"/>, soit <see cref="Error"/> (en français).
/// <see cref="Warnings"/> liste ce qui a été écarté ou corrigé sans rejeter la disposition.
/// </summary>
public sealed record LayoutLoadResult(DashboardLayout? Layout, string? Error, IReadOnlyList<string> Warnings);

/// <summary>Lecture et validation d'une disposition en JSON, communes à <see cref="LayoutStore"/> et <see cref="LayoutTransfer"/>.</summary>
internal static class LayoutJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Avertissement quand une <c>Appearance</c> illisible est abandonnée.</summary>
    internal const string AppearanceDropped =
        "Réglages d'apparence illisibles dans layout.json : apparence par défaut utilisée.";

    internal const string MaxFpsInvalid = "Plafond d'images invalide : 12 images/s utilisées.";
    internal const string ShortcutsDropped = "Raccourcis illisibles dans layout.json : raccourcis par défaut utilisés.";
    internal const string Unreadable = "Le fichier layout.json est illisible (JSON invalide).";
    internal const int MinFps = 1;
    internal const int MaxFpsLimit = 12;

    /// <summary>
    /// Lit et valide. Ne lève jamais d'exception ; ne touche à aucun fichier. <c>Appearance</c>, <c>Shortcuts</c> (un
    /// par un) et <c>MaxFps</c> sont lus avec indulgence : ce qui est illisible est écarté avec un avertissement, la
    /// disposition est gardée. Chaque avertissement est aussi transmis à <paramref name="warn"/>.
    /// </summary>
    internal static LayoutLoadResult Parse(string json, Action<string>? warn)
    {
        var warnings = new List<string>();
        void Warn(string message)
        {
            warnings.Add(message);
            warn?.Invoke(message);
        }

        try
        {
            var layout = Deserialize(json, Warn);
            if (layout is null) return Fail(Unreadable, warnings);

            var error = Validate(layout);
            return error is null
                ? new LayoutLoadResult(layout, null, warnings)
                : Fail(error, warnings);
        }
        catch (Exception)
        {
            // Toute erreur de lecture (JSON, type incohérent, débordement...) : illisible.
            return Fail(Unreadable, warnings);
        }
    }

    private static LayoutLoadResult Fail(string error, List<string> warnings) => new(null, error, warnings);

    private static DashboardLayout? Deserialize(string json, Action<string> warn)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null; // racine non-objet

            // Lever ici (clé en double) vaut ArgumentException, à la première lecture d'une propriété : voir plus bas.
            var hasAppearance = Take(root, nameof(DashboardLayout.Appearance), out var appearance);
            var hasShortcuts = Take(root, nameof(DashboardLayout.Shortcuts), out var shortcuts);
            Take(root, nameof(DashboardLayout.MaxFps), out var maxFps);

            var layout = root.Deserialize<DashboardLayout>(Options);
            if (layout is null) return null;

            return layout with
            {
                Appearance = hasAppearance ? ReadAppearance(appearance, warn) : null,
                Shortcuts = hasShortcuts ? ReadShortcuts(shortcuts, warn) : null,
                MaxFps = ReadMaxFps(maxFps, warn),
            };
        }
        catch (ArgumentException)
        {
            // Un nom de propriété en double : JsonNode refuse, le sérialiseur l'acceptait (la dernière valeur gagne) et
            // les fichiers d'avant le plan 4 doivent rester lus. Chemin d'avant, sans lecture indulgente.
            var legacy = JsonSerializer.Deserialize<DashboardLayout>(json, Options);
            return legacy is null
                ? null
                : legacy with { Shortcuts = DropNullShortcuts(legacy.Shortcuts, warn), MaxFps = CheckMaxFps(legacy.MaxFps, warn) };
        }
    }

    /// <summary>Retire la propriété de l'objet pour que le sérialiseur ne la lise pas (elle est lue avec indulgence).</summary>
    private static bool Take(JsonObject root, string name, out JsonNode? node)
    {
        var found = root.TryGetPropertyValue(name, out node);
        root.Remove(name);
        return found;
    }

    private static AppearanceSettings? ReadAppearance(JsonNode? node, Action<string> warn)
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

        warn(AppearanceDropped);
        return null;
    }

    private static int? ReadMaxFps(JsonNode? node, Action<string> warn)
    {
        if (node is null) return null; // absent, ou null voulu
        if (node is JsonValue value && value.TryGetValue<long>(out var n) && n is >= MinFps and <= MaxFpsLimit)
            return (int)n;

        warn(MaxFpsInvalid);
        return null;
    }

    private static int? CheckMaxFps(int? maxFps, Action<string> warn)
    {
        if (maxFps is null or (>= MinFps and <= MaxFpsLimit)) return maxFps;

        warn(MaxFpsInvalid);
        return null;
    }

    /// <summary>Chemin d'avant : les entrées <c>null</c> sont écartées avec le même avertissement que la lecture indulgente.</summary>
    private static IReadOnlyList<ShortcutBinding>? DropNullShortcuts(IReadOnlyList<ShortcutBinding>? shortcuts, Action<string> warn)
    {
        if (shortcuts is null || !shortcuts.Contains(null!)) return shortcuts;

        var kept = new List<ShortcutBinding>();
        foreach (var binding in shortcuts)
        {
            if (binding is not null) kept.Add(binding);
            else warn("Raccourci « ? » ignoré : touches absentes.");
        }

        return kept;
    }

    /// <summary>Un raccourci illisible est écarté (avec un avertissement) ; il ne fait pas rejeter la disposition.</summary>
    private static List<ShortcutBinding>? ReadShortcuts(JsonNode? node, Action<string> warn)
    {
        if (node is null) return null; // "Shortcuts": null, voulu
        if (node is not JsonArray array)
        {
            warn(ShortcutsDropped);
            return null;
        }

        var bindings = new List<ShortcutBinding>();
        foreach (var item in array)
        {
            var binding = ReadShortcut(item, out var reason, out var keys);
            if (binding is not null) bindings.Add(binding);
            else warn($"Raccourci « {keys} » ignoré : {reason}.");
        }

        return bindings;
    }

    private static ShortcutBinding? ReadShortcut(JsonNode? item, out string reason, out string keys)
    {
        keys = "?";
        reason = "";
        var obj = item as JsonObject;
        if (obj?[nameof(ShortcutBinding.Keys)] is not JsonValue keysValue || !keysValue.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            reason = "touches absentes";
            return null;
        }

        keys = text;
        if (!IsKnownCommand(obj[nameof(ShortcutBinding.Command)]))
        {
            reason = "commande inconnue";
            return null;
        }

        if (!ShortcutKeys.TryParse(text, out _, out _))
        {
            reason = "touches non reconnues";
            return null;
        }

        try
        {
            var binding = obj.Deserialize<ShortcutBinding>(Options);
            if (binding is not null) return binding;
        }
        catch (Exception)
        {
            // Index ou Target du mauvais type : même sort.
        }

        reason = "réglage illisible";
        return null;
    }

    private static bool IsKnownCommand(JsonNode? node)
    {
        if (node is not JsonValue value) return false;
        if (value.TryGetValue<string>(out var name))
            return !int.TryParse(name, out _) && Enum.TryParse<ModuleCommandKind>(name, ignoreCase: true, out var kind) &&
                   Enum.IsDefined(kind);
        return value.TryGetValue<int>(out var number) && Enum.IsDefined((ModuleCommandKind)number);
    }

    /// <summary>La raison du refus, en français, ou <c>null</c> si la disposition est valide.</summary>
    private static string? Validate(DashboardLayout layout)
    {
        if (layout.Rotation is not (0 or 180)) return $"Rotation {layout.Rotation}° refusée : 0 ou 180.";
        if (layout.Placements is null) return Unreadable;

        var placements = layout.Placements;
        foreach (var p in placements)
        {
            if (p is null) return Unreadable;
            if (string.IsNullOrWhiteSpace(p.TypeId)) return "Module sans type.";
            if (p.Settings.ValueKind == JsonValueKind.Undefined) return $"Module sans réglages : {p.TypeId}.";
            if (!LayoutValidator.IsInBounds(p)) return $"Module hors du damier : {p.TypeId}.";
        }

        if (layout.Shelf is { } shelf)
        {
            foreach (var p in shelf)
            {
                if (p is null) return "Module retiré illisible.";
                if (string.IsNullOrWhiteSpace(p.TypeId)) return "Module retiré sans type.";
                if (p.Settings.ValueKind == JsonValueKind.Undefined) return $"Module retiré sans réglages : {p.TypeId}.";
                if (p.Columns < 1 || p.Rows < 1) return $"Module retiré de taille invalide : {p.TypeId}.";
            }
        }

        var ids = placements.Concat(layout.Shelf ?? []).Select(p => p.InstanceId).ToList();
        if (ids.Distinct().Count() != ids.Count) return "Identifiant de module en double.";

        for (var i = 0; i < placements.Count; i++)
            for (var j = i + 1; j < placements.Count; j++)
                if (LayoutValidator.Intersects(placements[i], placements[j]))
                    return $"Deux modules se chevauchent : {placements[i].TypeId} et {placements[j].TypeId}.";

        return null;
    }
}
