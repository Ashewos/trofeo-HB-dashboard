namespace Trofeo.Modules.NowPlaying.Tests;

public class MediaTimelineTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static MediaSnapshot Snap(bool playing, int positionSeconds, int durationSeconds) =>
        new("T", "A", "App", TimeSpan.FromSeconds(positionSeconds), TimeSpan.FromSeconds(durationSeconds), At, playing, null);

    [Fact]
    public void Current_Playing_Extrapolates() =>
        Assert.Equal(TimeSpan.FromSeconds(50), MediaTimeline.Current(Snap(true, 40, 200), At.AddSeconds(10)));

    [Fact]
    public void Current_Paused_Frozen() =>
        Assert.Equal(TimeSpan.FromSeconds(40), MediaTimeline.Current(Snap(false, 40, 200), At.AddSeconds(10)));

    [Fact]
    public void Current_ClampedToDuration() =>
        Assert.Equal(TimeSpan.FromSeconds(200), MediaTimeline.Current(Snap(true, 195, 200), At.AddSeconds(60)));

    [Fact]
    public void Current_UnknownDuration_NotClampedAbove() =>
        Assert.Equal(TimeSpan.FromSeconds(260), MediaTimeline.Current(Snap(true, 200, 0), At.AddSeconds(60)));

    [Fact]
    public void Current_NowBeforePositionAt_ClampedToZero() =>
        Assert.Equal(TimeSpan.Zero, MediaTimeline.Current(Snap(true, 5, 200), At.AddSeconds(-60)));

    [Fact]
    public void Format_UnderAndOverOneHour()
    {
        Assert.Equal("0:00", MediaTimeline.Format(TimeSpan.Zero));
        Assert.Equal("0:09", MediaTimeline.Format(TimeSpan.FromSeconds(9)));
        Assert.Equal("1:24", MediaTimeline.Format(TimeSpan.FromSeconds(84)));
        Assert.Equal("59:59", MediaTimeline.Format(TimeSpan.FromSeconds(3599)));
        Assert.Equal("1:00:00", MediaTimeline.Format(TimeSpan.FromHours(1)));
        Assert.Equal("1:02:03", MediaTimeline.Format(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Format_TruncatesFractionsAndClampsNegative()
    {
        Assert.Equal("1:24", MediaTimeline.Format(TimeSpan.FromSeconds(84.9)));
        Assert.Equal("0:00", MediaTimeline.Format(TimeSpan.FromSeconds(-5)));
    }
}
