namespace Trofeo.Modules.NowPlaying;

/// <summary>
/// Ce qui est en lecture à un instant donné. Immuable : la source en publie un nouveau à chaque changement.
/// </summary>
/// <param name="AppName">Nom lisible de l'application (voir <see cref="AppNames.Friendly"/>).</param>
/// <param name="Position">Position de lecture à l'instant <paramref name="PositionAt"/>.</param>
/// <param name="Duration">Durée totale ; zéro si inconnue (flux en direct).</param>
/// <param name="PositionAt">Instant auquel <paramref name="Position"/> a été relevée, pour extrapoler pendant la lecture.</param>
/// <param name="Artwork">Pochette encodée (PNG, JPEG…), ou <c>null</c>. La référence du tableau identifie l'image : elle ne change que si la pochette change.</param>
public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string AppName,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionAt,
    bool IsPlaying,
    byte[]? Artwork);
