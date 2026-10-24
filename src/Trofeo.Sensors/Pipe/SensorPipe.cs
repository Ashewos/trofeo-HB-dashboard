using System.Text.Json;

namespace Trofeo.Sensors;

/// <summary>
/// Tube nommé à sens unique par lequel l'assistant élevé publie ses relevés : une ligne JSON UTF-8 par relevé,
/// <c>{"cpuName":…,"gpuName":…,"elevated":true,"values":{"cpu.load":34.0,…}}</c>.
/// </summary>
public static class SensorPipe
{
    /// <summary>Nom du tube (sans le préfixe <c>\\.\pipe\</c>).</summary>
    public const string Name = "trofeo-HB-dashboard.sensors";

    /// <summary>Longueur maximale d'une ligne, fin de ligne exclue. Au-delà, le client coupe la connexion.</summary>
    public const int MaxLineBytes = 65536;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // L'ordre des propriétés est celui du format documenté ci-dessus.
    private sealed record Wire(string? CpuName, string? GpuName, bool Elevated, Dictionary<string, double>? Values);

    /// <summary>Une ligne JSON terminée par <c>\n</c>. Les valeurs non finies (NaN, ±∞), que JSON ne sait pas écrire, sont omises.</summary>
    internal static byte[] Serialize(SensorSnapshot snapshot)
    {
        var values = new Dictionary<string, double>(snapshot.Values.Count);
        foreach (var (key, value) in snapshot.Values)
            if (double.IsFinite(value)) values[key] = value;

        var json = JsonSerializer.SerializeToUtf8Bytes(new Wire(snapshot.CpuName, snapshot.GpuName, snapshot.Elevated, values), Json);
        var line = new byte[json.Length + 1];
        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        return line;
    }

    /// <summary>
    /// Relit une ligne reçue, ou <c>null</c> si elle n'est pas un relevé valide. Les champs inconnus sont ignorés ;
    /// <c>values</c> absent donne un dictionnaire vide.
    /// </summary>
    internal static SensorSnapshot? TryParse(ReadOnlySpan<byte> line)
    {
        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(line, Json);
            if (wire is null) return null;
            return new SensorSnapshot(wire.CpuName, wire.GpuName,
                new Dictionary<string, double>(wire.Values ?? [], StringComparer.Ordinal), wire.Elevated);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
