using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trofeo.StatusLine;

/// <summary>
/// Résultat d'une installation ou d'une désinstallation de la commande de barre d'état. <c>Updated</c> : la commande était
/// déjà la nôtre mais pointait vers un autre exécutable ; seul le chemin a été réécrit.
/// </summary>
public enum StatusLineInstallResult { Installed, AlreadyInstalled, Uninstalled, NotInstalled, InvalidSettings, Updated }

/// <summary>
/// Branche (ou débranche) <c>Trofeo.StatusLine.exe</c> dans le <c>settings.json</c> de Claude Code, de façon réversible.
/// Une commande déjà présente est conservée : la nôtre la précède dans un tube (<c>"exe" --tee | ( commande )</c>).
/// Si le fichier n'est pas lisible, rien n'est écrit.
/// </summary>
public sealed class StatusLineInstaller(string claudeHome)
{
    private const string Marker = "Trofeo.StatusLine";
    private const string ChainSeparator = " --tee | ";

    /// <summary>UTF-8 strict : un octet invalide fait échouer la lecture plutôt que d'être remplacé puis réécrit.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // garde lisibles |, &, accents…
    };

    /// <summary>Le <c>settings.json</c> de Claude Code, tel que ce programme le lit et le réécrit.</summary>
    public string SettingsPath => Path.Combine(claudeHome, "settings.json");

    /// <summary>La copie du fichier d'origine, faite à la première installation.</summary>
    public string BackupPath => SettingsPath + ".trofeo-bak";
    private string PreviousPath => Path.Combine(claudeHome, "trofeo-HB-dashboard", "statusline-previous.json");

    public StatusLineInstallResult Install(string exePath)
    {
        if (!TryReadSettings(out var root)) return StatusLineInstallResult.InvalidSettings;
        if (IsOurs(root["statusLine"])) return RefreshExePath(root, NormalizeExePath(exePath));

        // Sauvegardes : copie du fichier d'origine la première fois seulement ; ancien nœud à chaque installation.
        if (File.Exists(SettingsPath) && !File.Exists(BackupPath)) File.Copy(SettingsPath, BackupPath);
        var previous = root["statusLine"];
        WriteAtomic(PreviousPath, new JsonObject { ["previous"] = previous?.DeepClone() });

        root["statusLine"] = BuildStatusLine(previous, NormalizeExePath(exePath));
        WriteAtomic(SettingsPath, root);
        return StatusLineInstallResult.Installed;
    }

    /// <summary>
    /// Notre commande est déjà là : si l'exécutable a changé de place, on remplace seulement le premier segment entre
    /// guillemets (le tube vers l'ancienne commande et les autres champs restent). Une commande qui ne commence pas par un
    /// segment entre guillemets a été modifiée à la main : on ne devine pas.
    /// </summary>
    private StatusLineInstallResult RefreshExePath(JsonObject root, string exe)
    {
        var statusLine = (JsonObject)root["statusLine"]!; // IsOurs a vérifié qu'il porte une commande
        var command = CommandOf(statusLine)!;
        var end = command.StartsWith('"') ? command.IndexOf('"', 1) : -1;
        // Notre marque doit être dans ce premier segment lui-même : une commande qui la cite plus loin n'est pas la nôtre.
        if (end < 0 || !command[1..end].Contains(Marker, StringComparison.OrdinalIgnoreCase)) return StatusLineInstallResult.AlreadyInstalled;
        if (string.Equals(command[1..end], exe, StringComparison.OrdinalIgnoreCase)) return StatusLineInstallResult.AlreadyInstalled;

        statusLine["command"] = $"\"{exe}\"" + command[(end + 1)..];
        WriteAtomic(SettingsPath, root);
        return StatusLineInstallResult.Updated;
    }

    public StatusLineInstallResult Uninstall()
    {
        if (!TryReadSettings(out var root)) return StatusLineInstallResult.InvalidSettings;
        if (!IsOurs(root["statusLine"])) return StatusLineInstallResult.NotInstalled;

        var chained = CommandOf(root["statusLine"])!.Contains(ChainSeparator, StringComparison.Ordinal);
        JsonNode? previous = null;
        if (File.Exists(PreviousPath))
        {
            // Une sauvegarde abîmée : on ne devine pas, on ne touche à rien.
            if (!TryParseObject(PreviousPath, out var saved)) return StatusLineInstallResult.InvalidSettings;
            previous = saved["previous"]?.DeepClone();
        }

        // Notre commande enveloppe celle d'origine, et la sauvegarde manque ou n'en dit rien : ne pas jeter l'originale.
        if (previous is null && chained) return StatusLineInstallResult.InvalidSettings;

        if (previous is null) root.Remove("statusLine"); else root["statusLine"] = previous;
        WriteAtomic(SettingsPath, root);
        File.Delete(PreviousPath);
        return StatusLineInstallResult.Uninstalled;
    }

    /// <summary>
    /// La commande de barre d'état actuelle de <c>settings.json</c> (la nôtre ou une autre), ou <c>null</c> s'il n'y en a
    /// pas ou si le fichier n'est pas lisible.
    /// </summary>
    public string? CurrentCommand() => TryReadSettings(out var root) ? CommandOf(root["statusLine"]) : null;

    public bool IsInstalled() => TryReadSettings(out var root) && IsOurs(root["statusLine"]);

    /// <summary>Fichier absent : objet vide. Sinon un objet JSON (commentaires et virgules finales admis).</summary>
    private bool TryReadSettings(out JsonObject root)
    {
        if (!File.Exists(SettingsPath))
        {
            root = new JsonObject();
            return true;
        }
        return TryParseObject(SettingsPath, out root);
    }

    private static bool TryParseObject(string path, out JsonObject root)
    {
        try
        {
            if (JsonNode.Parse(ReadStrictUtf8(path), documentOptions: ReadOptions) is JsonObject obj)
            {
                RejectDuplicateKeys(obj);
                root = obj;
                return true;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or DecoderFallbackException
                                      or ArgumentException) // ArgumentException : clé dupliquée
        {
            // Illisible : traité comme invalide.
        }
        root = new JsonObject();
        return false;
    }

    /// <summary>
    /// <c>JsonNode</c> ne voit une clé dupliquée (<c>{"a":1,"a":2}</c>) qu'au premier accès à l'objet, où il lève une
    /// <see cref="ArgumentException"/> : on parcourt tout l'arbre tout de suite pour que l'erreur survienne ici, avant
    /// toute écriture.
    /// </summary>
    private static void RejectDuplicateKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var child in obj) RejectDuplicateKeys(child.Value);
                break;
            case JsonArray array:
                foreach (var child in array) RejectDuplicateKeys(child);
                break;
        }
    }

    private static string ReadStrictUtf8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0; // BOM éventuel
        return StrictUtf8.GetString(bytes, start, bytes.Length - start);
    }

    private static bool IsOurs(JsonNode? statusLine) =>
        CommandOf(statusLine) is { } command && command.Contains(Marker, StringComparison.OrdinalIgnoreCase);

    private static string? CommandOf(JsonNode? statusLine) =>
        statusLine is JsonObject o && o["command"] is JsonValue v && v.TryGetValue<string>(out var command) ? command : null;

    private static JsonNode BuildStatusLine(JsonNode? previous, string exe)
    {
        var ours = $"\"{exe}\"";
        if (previous is JsonObject old
            && old["type"] is JsonValue type && type.TryGetValue<string>(out var kind) && kind == "command"
            && CommandOf(old) is { } oldCommand && !string.IsNullOrWhiteSpace(oldCommand))
        {
            var chained = (JsonObject)old.DeepClone(); // garde padding, refreshInterval…
            // Parenthèses : sans elles, « | » lie plus fort que « && », « || » et « ; » et priverait la fin de l'entrée.
            chained["command"] = $"{ours}{ChainSeparator}( {oldCommand} )";
            return chained;
        }
        return new JsonObject { ["type"] = "command", ["command"] = ours };
    }

    /// <summary>Chemin absolu avec des <c>/</c> (la commande passe par un interpréteur de commandes).</summary>
    private static string NormalizeExePath(string exePath)
    {
        var path = exePath.Replace('\\', '/');
        var driveRooted = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';
        if (!driveRooted && !path.StartsWith('/')) path = Path.GetFullPath(exePath).Replace('\\', '/');
        return path;
    }

    private static void WriteAtomic(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, node.ToJsonString(WriteOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
