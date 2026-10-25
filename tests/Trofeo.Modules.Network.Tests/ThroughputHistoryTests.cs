namespace Trofeo.Modules.Network.Tests;

public class ThroughputHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void History_DropsOlderThanWindow()
    {
        var history = new ThroughputHistory(TimeSpan.FromMinutes(3));
        history.Add(T0, new ThroughputSample(1, 1));
        history.Add(T0 + TimeSpan.FromMinutes(1), new ThroughputSample(2, 2));
        history.Add(T0 + TimeSpan.FromMinutes(3), new ThroughputSample(3, 3)); // T0 est pile à la limite : conservé

        Assert.Equal(3, history.Snapshot().Count);

        history.Add(T0 + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(1), new ThroughputSample(4, 4));

        var kept = history.Snapshot();
        Assert.Equal(new[] { 2d, 3d, 4d }, kept.Select(item => item.Sample.DownBytesPerSecond));
        Assert.Equal(T0 + TimeSpan.FromMinutes(1), kept[0].At);
    }

    [Fact]
    public void History_Snapshot_IsCopy()
    {
        var history = new ThroughputHistory(TimeSpan.FromMinutes(3));
        history.Add(T0, new ThroughputSample(1, 1));

        var before = history.Snapshot();
        history.Add(T0 + TimeSpan.FromSeconds(1), new ThroughputSample(2, 2));

        Assert.Single(before);
        Assert.Equal(2, history.Snapshot().Count);
    }
}
