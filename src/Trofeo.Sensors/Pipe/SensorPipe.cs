using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trofeo.Sensors;

/// <summary>
/// Tube nommé à sens unique par lequel l'assistant élevé publie ses relevés : une ligne JSON UTF-8 par relevé,
/// <c>{"cpuName":…,"gpuName":…,"elevated":true,"values":{"cpu.load":34.0,…},"gpus":[{"name":…,"values":{…}},…]}</c>.
/// <c>gpus</c> (toutes les cartes) est facultatif : un assistant plus ancien ne l'écrit pas, et un service plus ancien l'ignore.
/// </summary>
public static class SensorPipe
{
    /// <summary>Nom du tube (sans le préfixe <c>\\.\pipe\</c>).</summary>
    public const string Name = "trofeo-HB-dashboard.sensors";

    /// <summary>Longueur maximale d'une ligne, fin de ligne exclue. Au-delà, le client coupe la connexion.</summary>
    public const int MaxLineBytes = 65536;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // L'ordre des propriétés est celui du format documenté ci-dessus.
    private sealed record Wire(string? CpuName, string? GpuName, bool Elevated, Dictionary<string, double>? Values)
    {
        // Absent du JSON (et non « null ») quand le relevé ne liste pas les cartes : la ligne reste celle de l'ancien format.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<WireGpu>? Gpus { get; init; }
    }

    private sealed record WireGpu(string? Name, Dictionary<string, double>? Values);

    private static Dictionary<string, double> Finite(IReadOnlyDictionary<string, double> source)
    {
        var values = new Dictionary<string, double>(source.Count);
        foreach (var (key, value) in source)
            if (double.IsFinite(value)) values[key] = value;
        return values;
    }

    /// <summary>Une ligne JSON terminée par <c>\n</c>. Les valeurs non finies (NaN, ±∞), que JSON ne sait pas écrire, sont omises.</summary>
    internal static byte[] Serialize(SensorSnapshot snapshot)
    {
        var wire = new Wire(snapshot.CpuName, snapshot.GpuName, snapshot.Elevated, Finite(snapshot.Values))
        {
            Gpus = snapshot.Gpus?.Select(g => new WireGpu(g.Name, Finite(g.Values))).ToList(),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(wire, Json);
        var line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return line;
    }

    /// <summary>
    /// Relit une ligne reçue, ou <c>null</c> si elle n'est pas un relevé valide. Les champs inconnus sont ignorés ;
    /// <c>values</c> absent donne un dictionnaire vide, <c>gpus</c> absent donne <c>null</c>.
    /// </summary>
    internal static SensorSnapshot? TryParse(ReadOnlySpan<byte> line)
    {
        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(line, Json);
            if (wire is null) return null;
            // Une carte sans nom est écartée : on ne pourrait pas la choisir.
            var gpus = wire.Gpus?.Where(g => !string.IsNullOrEmpty(g?.Name))
                .Select(g => new GpuReading(g!.Name!, new Dictionary<string, double>(g.Values ?? [], StringComparer.Ordinal)))
                .ToList();
            return new SensorSnapshot(wire.CpuName, wire.GpuName,
                new Dictionary<string, double>(wire.Values ?? [], StringComparer.Ordinal), wire.Elevated, gpus);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
