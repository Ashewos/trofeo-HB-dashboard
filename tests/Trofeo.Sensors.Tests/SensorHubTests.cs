using Microsoft.Extensions.Time.Testing;

namespace Trofeo.Sensors.Tests;

/// <summary>Choix de la source (assistant élevé ou lecture locale), sans tube ni matériel : temps simulé, flux et lecteur factices.</summary>
public class SensorHubTests
{
    private sealed class FakeStream : ISnapshotStream
    {
        public event EventHandler<SensorSnapshot>? Received;
        public int Subscribers => Received?.GetInvocationList().Length ?? 0;
        public void Send(SensorSnapshot snapshot) => Received?.Invoke(this, snapshot);
    }

    private sealed class FakeReader(string cpuName, Func<int, bool>? fails = null) : ISensorReader
    {
        public int Reads;
        public bool Disposed;

        public SensorSnapshot Read()
        {
            Reads++;
            if (fails?.Invoke(Reads) == true) throw new InvalidOperationException("capteur en panne");
            return new SensorSnapshot(cpuName, null, new Dictionary<string, double> { [SensorKeys.CpuLoad] = Reads }, Elevated: false);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Rig
    {
        public readonly FakeTimeProvider Time = new();
        public readonly FakeStream Helper = new();
        public readonly List<FakeReader> Readers = [];
        public readonly SensorHub Hub;
        public int Updates;

        public Rig(Func<int, bool>? fails = null)
        {
            Hub = new SensorHub(() => { var r = new FakeReader("local", fails); Readers.Add(r); return r; }, Helper, Time);
            Hub.Updated += (_, _) => Interlocked.Increment(ref Updates);
        }

        public int LocalReads => Readers.Sum(r => r.Reads);
        public void Advance(int seconds) => Time.Advance(TimeSpan.FromSeconds(seconds));
    }

    private static SensorSnapshot FromHelper() =>
        new("helper", "gpu", new Dictionary<string, double> { [SensorKeys.CpuTemp] = 70 }, Elevated: true);

    [Fact]
    public void Hub_NotAcquired_ReadsNothing()
    {
        var rig = new Rig();

        rig.Advance(10);
        rig.Helper.Send(FromHelper());

        Assert.Empty(rig.Readers);
        Assert.Null(rig.Hub.Latest);
        Assert.Equal(0, rig.Updates);
        Assert.Equal(0, rig.Helper.Subscribers);
    }

    [Fact]
    public void Hub_NoHelper_ReadsLocalEverySecond()
    {
        var rig = new Rig();
        using var lease = rig.Hub.Acquire();
        Assert.Empty(rig.Readers); // lecteur créé à la première utilisation seulement

        rig.Advance(1);
        Assert.Equal(1, rig.LocalReads);
        Assert.Equal(1, rig.Updates);
        Assert.Equal("local", rig.Hub.Latest!.CpuName);
        Assert.Equal(1, rig.Hub.Latest!.Values[SensorKeys.CpuLoad]);

        rig.Advance(1);
        rig.Advance(1);
        Assert.Equal(3, rig.LocalReads);
        Assert.Equal(3, rig.Updates);
        Assert.Single(rig.Readers);
    }

    [Fact]
    public void Hub_HelperActive_SkipsLocal()
    {
        var rig = new Rig();
        using var lease = rig.Hub.Acquire();

        for (var i = 0; i < 5; i++)
        {
            rig.Helper.Send(FromHelper());
            rig.Advance(1);
        }

        Assert.Equal(0, rig.LocalReads);
        Assert.Empty(rig.Readers);
        Assert.Equal(5, rig.Updates); // un événement par relevé de l'assistant, publié à la réception
        Assert.Equal("helper", rig.Hub.Latest!.CpuName);
        Assert.True(rig.Hub.Latest.Elevated);
    }

    [Fact]
    public void Hub_HelperSilent_FallsBackToLocal()
    {
        var rig = new Rig();
        using var lease = rig.Hub.Acquire();
        rig.Helper.Send(FromHelper());

        rig.Advance(1);
        rig.Advance(1);
        Assert.Equal(0, rig.LocalReads); // 2 s de silence : l'assistant compte encore comme actif

        rig.Advance(1); // 3 s
        Assert.Equal(1, rig.LocalReads);
        Assert.Equal("local", rig.Hub.Latest!.CpuName);

        rig.Helper.Send(FromHelper()); // l'assistant revient : la lecture locale cesse
        rig.Advance(1);
        Assert.Equal(1, rig.LocalReads);
        Assert.Equal("helper", rig.Hub.Latest!.CpuName);
    }

    [Fact]
    public void Hub_LastRelease_StopsReading()
    {
        var rig = new Rig();
        var first = rig.Hub.Acquire();
        var second = rig.Hub.Acquire();
        rig.Advance(1);
        Assert.Equal(1, rig.LocalReads);

        first.Dispose();
        first.Dispose(); // une double libération ne doit pas compter deux fois
        rig.Advance(1);
        Assert.Equal(2, rig.LocalReads); // le second preneur tient encore le hub

        second.Dispose();
        Assert.True(rig.Readers[0].Disposed);
        Assert.Null(rig.Hub.Latest);
        Assert.Equal(0, rig.Helper.Subscribers);
        var updates = rig.Updates;

        rig.Advance(10);
        rig.Helper.Send(FromHelper());

        Assert.Equal(2, rig.LocalReads);
        Assert.Equal(updates, rig.Updates);
    }

    [Fact]
    public void Hub_Reacquire_CreatesFreshReader()
    {
        var rig = new Rig();
        rig.Hub.Acquire().Dispose();
        using var again = rig.Hub.Acquire();

        rig.Advance(1);

        Assert.Equal(1, rig.LocalReads);
        Assert.Equal(1, rig.Updates);
    }

    [Fact]
    public void Hub_LocalReaderThrows_KeepsRunning()
    {
        var rig = new Rig(fails: n => n <= 2);
        using var lease = rig.Hub.Acquire();

        rig.Advance(1);
        rig.Advance(1);
        Assert.Equal(0, rig.Updates);
        Assert.Null(rig.Hub.Latest);

        rig.Advance(1);
        Assert.Equal(1, rig.Updates);
        Assert.Equal(3, rig.Hub.Latest!.Values[SensorKeys.CpuLoad]);
    }

    [Fact]
    public void Hub_UpdatedHandlerThrows_DoesNotBreakTheFeed()
    {
        var rig = new Rig();
        rig.Hub.Updated += (_, _) => throw new InvalidOperationException("abonné défaillant");
        using var lease = rig.Hub.Acquire();

        rig.Advance(1);
        rig.Advance(1);

        Assert.Equal(2, rig.LocalReads);
    }

    [Fact]
    public void Hub_Dispose_ReleasesEverything_AndRefusesNewLeases()
    {
        var rig = new Rig();
        var lease = rig.Hub.Acquire();
        rig.Advance(1);

        rig.Hub.Dispose();

        Assert.True(rig.Readers[0].Disposed);
        Assert.Equal(0, rig.Helper.Subscribers);
        Assert.Throws<ObjectDisposedException>(() => rig.Hub.Acquire());
        lease.Dispose(); // sans effet après la destruction du hub
    }
}
