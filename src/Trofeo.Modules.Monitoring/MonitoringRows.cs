using System.Globalization;
using SkiaSharp;
using Trofeo.Sensors;

namespace Trofeo.Modules.Monitoring;

/// <summary>Une ligne du module : libellé, barre (0 à 1, ou <c>null</c>), valeur et colonne secondaire (ou <c>null</c>).</summary>
public sealed record MonitoringRow(string Label, float? Bar, string Value, string? Secondary);

/// <summary>Construction des lignes du module Monitoring PC à partir d'un relevé, et place disponible pour les afficher.</summary>
public static class MonitoringRows
{
    /// <summary>Texte d'une valeur absente.</summary>
    public const string Dash = "—";

    // Dimensions de la mise en page, avant multiplication par l'échelle de texte.
    internal const float PaddingX = 22f;
    internal const float PaddingY = 18f;
    internal const float TitleSize = 18f;
    internal const float TitleGap = 8f;
    internal const float RowHeight = 40f;
    internal const float RowGap = 10f;

    private static readonly CultureInfo French = new("fr-FR");
    private static readonly string[] Ids = ["cpu", "gpu", "ram", "vram", "clock", "power", "fan", "disk"];

    /// <summary>Vrai si <paramref name="rowId"/> désigne une ligne disponible (en minuscules).</summary>
    public static bool IsKnown(string rowId) => Array.IndexOf(Ids, rowId) >= 0;

    /// <summary>Construit la ligne <paramref name="rowId"/> ; un relevé <c>null</c> ou un capteur absent donne « — ».</summary>
    /// <exception cref="ArgumentException">Identifiant de ligne inconnu.</exception>
    public static MonitoringRow Build(string rowId, SensorSnapshot? s) => rowId switch
    {
        "cpu" => Load("CPU", s, SensorKeys.CpuLoad, SensorKeys.CpuTemp),
        "gpu" => Load("GPU", s, SensorKeys.GpuLoad, SensorKeys.GpuTemp),
        "ram" => Memory("RAM", s, SensorKeys.RamUsed, SensorKeys.RamTotal, divisor: 1.0),
        // La VRAM est relevée en Mo ; on l'affiche en Go.
        "vram" => Memory("VRAM", s, SensorKeys.GpuVramUsed, SensorKeys.GpuVramTotal, divisor: 1024.0),
        "clock" => new MonitoringRow("Fréq.", null,
            Text(Get(s, SensorKeys.CpuClock), v => Decimal(v / 1000) + " GHz"),
            Text(Get(s, SensorKeys.GpuClock), v => Decimal(v / 1000) + " GHz")),
        "power" => new MonitoringRow("Conso", null,
            Text(Get(s, SensorKeys.CpuPower), v => Whole(v) + " W"),
            Text(Get(s, SensorKeys.GpuPower), v => Whole(v) + " W")),
        "fan" => new MonitoringRow("Ventil.", null, Text(Get(s, SensorKeys.GpuFan), v => Whole(v) + " tr/min"), null),
        "disk" => Load("Disque", s, SensorKeys.DiskUsed, SensorKeys.DiskTemp),
        _ => throw new ArgumentException($"Ligne inconnue : {rowId}", nameof(rowId)),
    };

    /// <summary>
    /// Nombre de lignes qui tiennent dans le module de rectangle <paramref name="moduleBounds"/> : on retire les marges
    /// (18 px en haut et en bas) et la ligne de titre (18 px plus 8 px d'écart), puis on empile des lignes de 40 px
    /// espacées de 10 px, le tout multiplié par <paramref name="textScale"/>.
    /// </summary>
    public static int Capacity(SKRect moduleBounds, float textScale)
    {
        var content = moduleBounds.Height - (2 * PaddingY + TitleSize + TitleGap) * textScale;
        var row = RowHeight * textScale;
        var gap = RowGap * textScale;
        if (content <= 0f || row <= 0f) return 0;
        return Math.Max(0, (int)Math.Floor((content + gap) / (row + gap)));
    }

    private static MonitoringRow Load(string label, SensorSnapshot? s, string loadKey, string tempKey)
    {
        var load = Get(s, loadKey);
        return new MonitoringRow(
            label,
            load is { } l ? Fraction(l, 100) : null,
            Text(load, v => Whole(v) + " %"),
            Text(Get(s, tempKey), v => Whole(v) + " °C"));
    }

    private static MonitoringRow Memory(string label, SensorSnapshot? s, string usedKey, string totalKey, double divisor)
    {
        var used = Get(s, usedKey);
        var total = Get(s, totalKey);
        return new MonitoringRow(
            label,
            used is { } u && total is { } t ? Fraction(u, t) : null,
            Text(used, v => Decimal(v / divisor)),
            Text(total, v => "/ " + Whole(v / divisor) + " Go"));
    }

    /// <summary>Valeur du capteur, ou <c>null</c> s'il est absent ou non numérique.</summary>
    private static double? Get(SensorSnapshot? s, string key) =>
        s is not null && s.Values.TryGetValue(key, out var v) && double.IsFinite(v) ? v : null;

    private static string Text(double? value, Func<double, string> format) => value is { } v ? format(v) : Dash;

    /// <summary>Rapport borné à [0, 1] ; <c>null</c> si le diviseur n'est pas strictement positif.</summary>
    private static float? Fraction(double value, double total) =>
        total > 0 ? (float)Math.Clamp(value / total, 0, 1) : null;

    private static string Whole(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", French);

    private static string Decimal(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", French);
}
