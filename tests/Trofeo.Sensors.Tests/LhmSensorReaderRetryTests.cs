using Microsoft.Extensions.Time.Testing;
using Trofeo.Core.Rendering;
using Trofeo.Sensors;

namespace Trofeo.Sensors.Tests;

/// <summary>Politique d'ouverture et de nouvel essai du lecteur, testée sans LibreHardwareMonitor.</summary>
public class LhmSensorReaderRetryTests
{
    private sealed class FakeSource(Func<int, bool> opens, List<FakeSource> log) : IHardwareSource
    {
        public bool Disposed;
        public int ReadCount;

        public void Open()
        {
            log.Add(this);
            if (!opens(log.Count)) throw new InvalidOperationException("pilote indisponible");
        }

        public IReadOnlyList<RawSensor> ReadRaw()
        {
            ReadCount++;
            return [new RawSensor(RawHardware.Cpu, "Fake CPU", RawKind.Load, "CPU Total", 42)];
        }

        public void Dispose() => Disposed = true;
    }

    private static (LhmSensorReader Reader, List<FakeSource> Attempts) Create(Func<int, bool> opensOnAttempt)
    {
        var attempts = new List<FakeSource>();
        return (new LhmSensorReader(() => new FakeSource(opensOnAttempt, attempts)), attempts);
    }

    [Fact]
    public void Read_OpensOnFirstRead_AndReportsOpen()
    {
        var (reader, attempts) = Create(_ => true);
        Assert.False(reader.IsOpen);

        var snap = reader.Read();

        Assert.True(reader.IsOpen);
        Assert.Single(attempts);
        Assert.Equal(42, snap.Values[SensorKeys.CpuLoad]);
        Assert.Equal("Fake CPU", snap.CpuName);
    }

    [Fact]
    public void Read_OpenFailure_DisposesHalfOpenedSource_AndStillReturnsRamAndDisk()
    {
        var (reader, attempts) = Create(_ => false);

        var snap = reader.Read();

        Assert.False(reader.IsOpen);
        Assert.True(attempts[0].Disposed);
        Assert.False(snap.Values.ContainsKey(SensorKeys.CpuLoad));
        Assert.True(snap.Values[SensorKeys.RamTotal] > 0);
    }

    [Fact]
    public void Read_OpenFailure_RetriesOnlyEveryRetryInterval()
    {
        var (reader, attempts) = Create(_ => false);

        for (var i = 0; i < LhmSensorReader.RetryIntervalReads; i++)
            reader.Read();
        Assert.Single(attempts); // un seul essai pendant l'intervalle

        reader.Read(); // la 31e lecture retente
        Assert.Equal(2, attempts.Count);

        for (var i = 0; i < LhmSensorReader.RetryIntervalReads - 1; i++)
            reader.Read();
        Assert.Equal(2, attempts.Count);
        reader.Read();
        Assert.Equal(3, attempts.Count);
    }

    [Fact]
    public void Read_RetrySucceeds_ThenStopsRetryingAndReadsSensors()
    {
        var (reader, attempts) = Create(attempt => attempt >= 2);

        reader.Read();
        Assert.False(reader.IsOpen);
        for (var i = 0; i < LhmSensorReader.RetryIntervalReads - 1; i++)
            reader.Read();
        Assert.False(reader.IsOpen);

        var snap = reader.Read();

        Assert.True(reader.IsOpen);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(42, snap.Values[SensorKeys.CpuLoad]);

        reader.Read();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(2, attempts[1].ReadCount);
    }

    [Fact]
    public void Dispose_ClosesOpenSource_AndIsIdempotent()
    {
        var (reader, attempts) = Create(_ => true);
        reader.Read();

        reader.Dispose();
        reader.Dispose();

        Assert.True(attempts[0].Disposed);
        Assert.False(reader.IsOpen);
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
    }

    private sealed class ThrowingSource : IHardwareSource
    {
        public void Open()
        {
        }

        public IReadOnlyList<RawSensor> ReadRaw() => throw new InvalidOperationException("capteur illisible");

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Read_FailingSensors_WarnsOncePerMinute()
    {
        var time = new FakeTimeProvider();
        var lines = new List<string>();
        using var reader = new LhmSensorReader(() => new ThrowingSource(), new RateLimitedErrorLog(time, lines.Add));

        for (var i = 0; i < 10; i++)
        {
            var snap = reader.Read(); // la lecture continue : RAM et disque seulement
            Assert.True(snap.Values[SensorKeys.RamTotal] > 0);
        }

        Assert.Single(lines);
        Assert.Contains("capteur illisible", lines[0]);

        time.Advance(TimeSpan.FromSeconds(61));
        reader.Read();

        Assert.Equal(2, lines.Count);
        Assert.Contains("répété", lines[1]);
    }
}
