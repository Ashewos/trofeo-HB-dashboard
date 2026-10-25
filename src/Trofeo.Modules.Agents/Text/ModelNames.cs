using System.Globalization;
using System.Text.RegularExpressions;

namespace Trofeo.Modules.Agents;

/// <summary>Noms de modèles lisibles pour l'écran : « claude-opus-5-5 » devient « Opus 5.5 ».</summary>
public static partial class ModelNames
{
    private const string Unknown = "—";

    [GeneratedRegex(@"-\d{8}$")]
    private static partial Regex DateSuffix();

    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\p{L}+$")]
    private static partial Regex Word();

    public static string Display(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId == "<synthetic>")
        {
            return Unknown;
        }

        var id = modelId.Trim().Replace("[1m]", "", StringComparison.OrdinalIgnoreCase);
        if (id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
        {
            id = id["claude-".Length..];
        }

        id = DateSuffix().Replace(id, "");
        var parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return modelId;
        }

        // gpt-5.1-codex-max : « GPT- » + numéro, puis les mots restants.
        if (parts[0].Equals("gpt", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length > 1 && char.IsDigit(parts[1][0])
                ? "GPT-" + parts[1] + string.Concat(parts.Skip(2).Select(p => " " + Capitalize(p)))
                : modelId;
        }

        // opus-5-5 : la famille, puis les nombres joints par « . ».
        if (!Word().IsMatch(parts[0]) || !parts.Skip(1).All(p => Number().IsMatch(p)))
        {
            return modelId;
        }

        return parts.Length == 1
            ? Capitalize(parts[0])
            : Capitalize(parts[0]) + " " + string.Join('.', parts.Skip(1));
    }

    private static string Capitalize(string word) =>
        char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
}
