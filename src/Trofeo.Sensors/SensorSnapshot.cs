namespace Trofeo.Sensors;

/// <summary>
/// Relevé neutre des capteurs à un instant donné. <paramref name="Values"/> est indexé par <see cref="SensorKeys"/> ;
/// une clé absente signifie « capteur indisponible » (jamais de valeur factice).
/// </summary>
/// <param name="CpuName">Nom brut du processeur, ou <c>null</c> s'il n'y en a pas.</param>
/// <param name="GpuName">Nom brut de la carte graphique retenue, ou <c>null</c> s'il n'y en a pas.</param>
/// <param name="Values">Valeurs dans les unités documentées sur <see cref="SensorKeys"/>.</param>
/// <param name="Elevated">Vrai si le relevé a été fait par un processus élevé (davantage de capteurs sont alors lisibles).</param>
/// <param name="Gpus">
/// Toutes les cartes graphiques détectées, dans l'ordre de préférence (la retenue d'abord), ou <c>null</c> quand le relevé
/// ne les liste pas (assistant d'une version antérieure) : il se lit alors comme avant, avec la seule carte retenue.
/// </param>
public sealed record SensorSnapshot(string? CpuName, string? GpuName, IReadOnlyDictionary<string, double> Values,
    bool Elevated, IReadOnlyList<GpuReading>? Gpus = null)
{
    /// <summary>
    /// Le relevé vu avec la carte nommée : ses valeurs <c>gpu.*</c> remplacent celles de la carte retenue et
    /// <see cref="GpuName"/> devient son nom. Nom <c>null</c> ou absent de <see cref="Gpus"/> (la casse compte) : le relevé tel quel.
    /// </summary>
    public SensorSnapshot ForGpu(string? name)
    {
        if (name is null || Gpus is null) return this;
        var gpu = Gpus.FirstOrDefault(g => g.Name == name);
        if (gpu is null) return this;

        // Les valeurs de la carte retenue partent toutes : une température absente chez l'autre carte ne doit pas rester.
        var values = new Dictionary<string, double>(Values.Count + gpu.Values.Count);
        foreach (var (key, value) in Values)
            if (!key.StartsWith(GpuKeyPrefix, StringComparison.Ordinal)) values[key] = value;
        foreach (var (key, value) in gpu.Values) values[key] = value;

        return this with { GpuName = gpu.Name, Values = values };
    }

    private const string GpuKeyPrefix = "gpu.";
}

/// <summary>Les valeurs d'une carte graphique, indexées par les clés <c>SensorKeys.Gpu*</c>.</summary>
/// <param name="Name">Nom brut du matériel, tel que le liste LibreHardwareMonitor.</param>
public sealed record GpuReading(string Name, IReadOnlyDictionary<string, double> Values);
