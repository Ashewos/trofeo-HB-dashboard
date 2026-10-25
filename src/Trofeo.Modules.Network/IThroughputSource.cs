using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>Débit instantané en octets par seconde, vers le PC (descendant) et depuis lui (montant).</summary>
public sealed record ThroughputSample(double DownBytesPerSecond, double UpBytesPerSecond);

/// <summary>
/// Résultat d'une lecture : l'échantillon (absent quand la source ne peut pas encore en donner un) et l'état de la
/// source, avec un éventuel message d'action pour l'utilisateur.
/// </summary>
public sealed record SourceReading(ThroughputSample? Sample, ModuleStatus Status);

/// <summary>
/// D'où vient le débit affiché : carte réseau du PC, Freebox, UniFi… Le module appelle <see cref="ReadAsync"/> à
/// chaque <see cref="Interval"/>, jamais deux lectures en même temps, et libère la source avec lui.
/// </summary>
public interface IThroughputSource : IDisposable
{
    /// <summary>Nom affiché en haut à gauche du module (« Ce PC », « Freebox »…).</summary>
    string Title { get; }

    /// <summary>Délai entre deux lectures.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Lit le débit courant. Une exception est traitée par le module comme « hors ligne » ; les états attendus
    /// (non configuré, action à faire sur la box…) se rendent par <see cref="SourceReading.Status"/>.
    /// </summary>
    Task<SourceReading> ReadAsync(CancellationToken ct);
}
