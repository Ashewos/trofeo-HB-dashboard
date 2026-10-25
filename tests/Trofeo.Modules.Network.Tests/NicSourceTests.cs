using Microsoft.Extensions.Time.Testing;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network.Tests;

public class NicSourceTests
{
    private sealed class Rig
    {
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        public List<InterfaceCounters> Counters { get; } = [];
        public NicSource Source { get; }

        public Rig() => Source = new NicSource(() => Counters.ToArray(), Time);

        public void Set(string id, long received, long sent)
        {
            Counters.RemoveAll(c => c.Id == id);
            Counters.Add(new InterfaceCounters(id, received, sent));
        }

        public Task<SourceReading> Read() => Source.ReadAsync(CancellationToken.None);
    }

    [Fact]
    public void NicSource_TitleAndInterval()
    {
        var rig = new Rig();

        Assert.Equal("Ce PC", rig.Source.Title);
        Assert.Equal(TimeSpan.FromSeconds(1), rig.Source.Interval);
    }

    [Fact]
    public async Task NicSource_FirstRead_NoSample()
    {
        var rig = new Rig();
        rig.Set("eth0", 1_000, 2_000);

        var reading = await rig.Read();

        Assert.Null(reading.Sample);
        Assert.Equal(new ModuleStatus(ModuleHealth.Online), reading.Status);
    }

    [Fact]
    public async Task NicSource_ComputesRate()
    {
        var rig = new Rig();
        rig.Set("eth0", 5_000_000, 100);
        await rig.Read();

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Set("eth0", 6_000_000, 100 + 250_000);
        var reading = await rig.Read();

        Assert.Equal(1_000_000d, reading.Sample!.DownBytesPerSecond);
        Assert.Equal(250_000d, reading.Sample.UpBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_RateIsDividedByElapsedTime()
    {
        var rig = new Rig();
        rig.Set("eth0", 0, 0);
        await rig.Read();

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        rig.Set("eth0", 2_000_000, 500_000);
        var reading = await rig.Read();

        Assert.Equal(1_000_000d, reading.Sample!.DownBytesPerSecond);
        Assert.Equal(250_000d, reading.Sample.UpBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_SumsAllInterfaces()
    {
        var rig = new Rig();
        rig.Set("eth0", 0, 0);
        rig.Set("wifi", 0, 0);
        await rig.Read();

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Set("eth0", 300, 10);
        rig.Set("wifi", 700, 20);
        var reading = await rig.Read();

        Assert.Equal(1_000d, reading.Sample!.DownBytesPerSecond);
        Assert.Equal(30d, reading.Sample.UpBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_CounterReset_NoSpike()
    {
        var rig = new Rig();
        rig.Set("eth0", 9_000_000_000, 8_000_000_000);
        await rig.Read();

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Set("eth0", 1_000, 500); // carte réinitialisée : les compteurs repartent de zéro
        var reset = await rig.Read();

        Assert.Equal(0d, reset.Sample!.DownBytesPerSecond);
        Assert.Equal(0d, reset.Sample.UpBytesPerSecond);

        // La lecture suivante repart de la nouvelle base, sans pic.
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Set("eth0", 3_000, 1_500);
        var next = await rig.Read();

        Assert.Equal(2_000d, next.Sample!.DownBytesPerSecond);
        Assert.Equal(1_000d, next.Sample.UpBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_NewInterface_IgnoredFirstTime()
    {
        var rig = new Rig();
        rig.Set("eth0", 0, 0);
        await rig.Read();

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Set("eth0", 100, 10);
        rig.Set("vpn", 50_000_000, 40_000_000); // apparue entre deux lectures : pas de point de départ
        var first = await rig.Read();

        Assert.Equal(100d, first.Sample!.DownBytesPerSecond);
        Assert.Equal(10d, first.Sample.UpBytesPerSecond);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Set("eth0", 200, 20);
        rig.Set("vpn", 50_000_500, 40_000_100);
        var second = await rig.Read();

        Assert.Equal(600d, second.Sample!.DownBytesPerSecond);
        Assert.Equal(110d, second.Sample.UpBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_VanishedInterface_IsIgnored()
    {
        var rig = new Rig();
        rig.Set("eth0", 0, 0);
        rig.Set("wifi", 0, 0);
        await rig.Read();

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Counters.RemoveAll(c => c.Id == "wifi");
        rig.Set("eth0", 400, 40);
        var reading = await rig.Read();

        Assert.Equal(400d, reading.Sample!.DownBytesPerSecond);
        Assert.Equal(40d, reading.Sample.UpBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_NoTimeElapsed_GivesNoSampleAndKeepsBaseline()
    {
        var rig = new Rig();
        rig.Set("eth0", 0, 0);
        await rig.Read();

        rig.Set("eth0", 500, 50);
        var same = await rig.Read(); // l'horloge n'a pas bougé

        Assert.Null(same.Sample);
        Assert.Equal(ModuleHealth.Online, same.Status.Health);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        var later = await rig.Read();

        Assert.Equal(500d, later.Sample!.DownBytesPerSecond);
    }

    [Fact]
    public async Task NicSource_CounterFunctionThrows_Propagates()
    {
        var source = new NicSource(() => throw new InvalidOperationException("boom"), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void ReadSystemCounters_DoesNotThrow()
    {
        var counters = NicSource.ReadSystemCounters();

        Assert.All(counters, c => Assert.False(string.IsNullOrEmpty(c.Id)));
    }
}
