namespace Trofeo.Sensors;

/// <summary>
/// Relevé neutre des capteurs à un instant donné. <paramref name="Values"/> est indexé par <see cref="SensorKeys"/> ;
/// une clé absente signifie « capteur indisponible » (jamais de valeur factice).
/// </summary>
/// <param name="CpuName">Nom brut du processeur, ou <c>null</c> s'il n'y en a pas.</param>
/// <param name="GpuName">Nom brut de la carte graphique retenue, ou <c>null</c> s'il n'y en a pas.</param>
/// <param name="Values">Valeurs dans les unités documentées sur <see cref="SensorKeys"/>.</param>
/// <param name="Elevated">Vrai si le relevé a été fait par un processus élevé (davantage de capteurs sont alors lisibles).</param>
public sealed record SensorSnapshot(string? CpuName, string? GpuName, IReadOnlyDictionary<string, double> Values, bool Elevated);
