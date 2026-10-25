using System.Globalization;

namespace Trofeo.Modules.NowPlaying;

/// <summary>Calculs de position et de durée affichés par le module.</summary>
public static class MediaTimeline
{
    /// <summary>
    /// Position de lecture à l'instant <paramref name="now"/> : la position relevée, avancée du temps écoulé depuis
    /// <see cref="MediaSnapshot.PositionAt"/> si la lecture est en cours. Bornée à [0, durée] quand la durée est connue.
    /// </summary>
    public static TimeSpan Current(MediaSnapshot s, DateTimeOffset now)
    {
        var position = s.IsPlaying ? s.Position + (now - s.PositionAt) : s.Position;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (s.Duration > TimeSpan.Zero && position > s.Duration) position = s.Duration;
        return position;
    }

    /// <summary>« 1:24 », ou « 1:02:03 » à partir d'une heure. Les fractions de seconde sont ignorées ; un temps négatif donne « 0:00 ».</summary>
    public static string Format(TimeSpan t)
    {
        var totalSeconds = (long)Math.Max(0, Math.Floor(t.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}
