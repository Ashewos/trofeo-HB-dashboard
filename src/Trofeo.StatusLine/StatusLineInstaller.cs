using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trofeo.StatusLine;

/// <summary>Résultat d'une installation ou d'une désinstallation de la commande de barre d'état.</summary>
public enum StatusLineInstallResult { Installed, AlreadyInstalled, Uninstalled, NotInstalled, InvalidSettings }

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

    private string SettingsPath => Path.Combine(claudeHome, "settings.json");
    private string BackupPath => SettingsPath + ".trofeo-bak";
    private string PreviousPath => Path.Combine(claudeHome, "trofeo-HB-dashboard", "statusline-previous.json");

    public StatusLineInstallResult Install(string exePath)
    {
        if (!TryReadSettings(out var root)) return StatusLineInstallResult.InvalidSettings;
        if (IsOurs(root["statusLine"])) return StatusLineInstallResult.AlreadyInstalled;

        // Sauvegardes : copie du fichier d'origine la première fois seulement ; ancien nœud à chaque installation.
        if (File.Exists(SettingsPath) && !File.Exists(BackupPath)) File.Copy(SettingsPath, BackupPath);
        var previous = root["statusLine"];
        WriteAtomic(PreviousPath, new JsonObject { ["previous"] = previous?.DeepClone() });

        root["statusLine"] = BuildStatusLine(previous, NormalizeExePath(exePath));
        WriteAtomic(SettingsPath, root);
        return StatusLineInstallResult.Installed;
    }

    public StatusLineInstallResult Uninstall()
    {
        if (!TryReadSettings(out var root)) return StatusLineInstallResult.InvalidSettings;
        if (!IsOurs(root["statusLine"])) return StatusLineInstallResult.NotInstalled;

        JsonNode? previous = null;
        if (File.Exists(PreviousPath))
        {
            // Une sauvegarde abîmée : on ne devine pas, on ne touche à rien.
            if (!TryParseObject(PreviousPath, out var saved)) return StatusLineInstallResult.InvalidSettings;
            previous = saved["previous"]?.DeepClone();
        }
        else if (CommandOf(root["statusLine"])!.Contains(ChainSeparator, StringComparison.Ordinal))
        {
            // Notre commande enveloppe celle d'origine, et sa sauvegarde a disparu : ne pas jeter l'originale.
            return StatusLineInstallResult.InvalidSettings;
        }

        if (previous is null) root.Remove("statusLine"); else root["statusLine"] = previous;
        WriteAtomic(SettingsPath, root);
        File.Delete(PreviousPath);
        return StatusLineInstallResult.Uninstalled;
    }

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
                root = obj;
                return true;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            // Illisible : traité comme invalide.
        }
        root = new JsonObject();
        return false;
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
