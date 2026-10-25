using System.Text.RegularExpressions;

namespace Trofeo.Sensors;

/// <summary>Noms de matériel raccourcis pour l'affichage.</summary>
public static partial class HardwareNames
{
    // Retraits sensibles à la casse. L'ordre compte : « with Radeon Graphics » doit partir avant « Radeon » et
    // « Graphics », sinon il resterait « with ».
    private static readonly string[] Symbols = ["with Radeon Graphics", "(R)", "(TM)", "®", "™"];

    // Chaînes CPUID Intel : « 12th Gen Intel(R) Core(TM) i5-12400F », « ... i7-9700K CPU @ 3.60GHz ».
    [GeneratedRegex(@"\b\d+(?:st|nd|rd|th) Gen\b")]
    private static partial Regex GenerationRegex();

    [GeneratedRegex(@"\bCPU\s*@\s*[\d.]+\s*GHz\b")]
    private static partial Regex CpuClockRegex();

    [GeneratedRegex(@"\bCPU\s*$")]
    private static partial Regex TrailingCpuRegex();

    [GeneratedRegex(@"\b\d+-Core\b")]
    private static partial Regex CoreCountRegex();

    [GeneratedRegex(@"\b(?:Intel|AMD|NVIDIA|GeForce|Radeon|Processor|Graphics)\b")]
    private static partial Regex VendorWordsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    /// <summary>« Core Ultra 7 265K · RTX 5070 » : processeur et carte graphique sans bruit de marque, ou « Monitoring » si aucun n'est connu.</summary>
    public static string Short(string? cpuName, string? gpuName)
    {
        var parts = new[] { Clean(cpuName), Clean(gpuName) }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "Monitoring" : string.Join(" · ", parts);
    }

    private static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        foreach (var symbol in Symbols)
            name = name.Replace(symbol, " ", StringComparison.Ordinal);
        name = GenerationRegex().Replace(name, " ");
        name = CpuClockRegex().Replace(name, " ");
        name = CoreCountRegex().Replace(name, " ");
        name = VendorWordsRegex().Replace(name, " ");
        name = SpacesRegex().Replace(name, " ").Trim();
        return TrailingCpuRegex().Replace(name, "").TrimEnd();
    }
}
