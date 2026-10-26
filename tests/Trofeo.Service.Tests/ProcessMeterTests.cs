using Microsoft.Extensions.Time.Testing;

namespace Trofeo.Display.Tests;

public sealed class ProcessMeterTests
{
    [Fact]
    public void ProcessMeter_ComputesMachinePercent()
    {
        var time = new FakeTimeProvider();
        var cpu = TimeSpan.Zero;
        var meter = new ProcessMeter(() => (cpu, 100, 200), time, processorCount: 4);
        meter.Read(); // première lecture : sert de point de départ

        time.Advance(TimeSpan.FromSeconds(2));
        cpu += TimeSpan.FromSeconds(1); // 1 s de CPU en 2 s sur 4 cœurs : 1 / (2 × 4)

        Assert.Equal(12.5, meter.Read().CpuPercent, 3);
    }

    [Fact]
    public void ProcessMeter_FirstRead_IsZeroCpuWithMemory()
    {
        var meter = new ProcessMeter(() => (TimeSpan.FromSeconds(30), 111, 222), new FakeTimeProvider(), 8);

        var info = meter.Read();

        Assert.Equal(0, info.CpuPercent);
        Assert.Equal(111, info.PrivateBytes);
        Assert.Equal(222, info.WorkingSetBytes);
    }

    [Fact]
    public void ProcessMeter_AveragesSinceThePreviousRead()
    {
        var time = new FakeTimeProvider();
        var cpu = TimeSpan.Zero;
        var meter = new ProcessMeter(() => (cpu, 0, 0), time, 2);
        meter.Read();

        time.Advance(TimeSpan.FromSeconds(1));
        cpu += TimeSpan.FromSeconds(1);
        Assert.Equal(50, meter.Read().CpuPercent, 3);

        time.Advance(TimeSpan.FromSeconds(4)); // la deuxième fenêtre ne compte que ses propres 4 s
        Assert.Equal(0, meter.Read().CpuPercent, 3);
    }

    [Fact]
    public void ProcessMeter_TwoReadsAtTheSameInstant_KeepTheLastValue()
    {
        var time = new FakeTimeProvider();
        var cpu = TimeSpan.Zero;
        var meter = new ProcessMeter(() => (cpu, 0, 0), time, 1);
        meter.Read();
        time.Advance(TimeSpan.FromSeconds(1));
        cpu += TimeSpan.FromSeconds(0.5);
        Assert.Equal(50, meter.Read().CpuPercent, 3);

        Assert.Equal(50, meter.Read().CpuPercent, 3); // pas de division par zéro
    }

    [Fact]
    public void ProcessMeter_CapsAtOneHundredPercent()
    {
        var time = new FakeTimeProvider();
        var cpu = TimeSpan.Zero;
        var meter = new ProcessMeter(() => (cpu, 0, 0), time, 1);
        meter.Read();

        time.Advance(TimeSpan.FromSeconds(1));
        cpu += TimeSpan.FromSeconds(1.5); // horloges peu précises : jamais plus de 100 %

        Assert.Equal(100, meter.Read().CpuPercent, 3);
    }

    [Fact]
    public void ProcessMeter_ConcurrentReaders_NeverMoveTheBaselineBackwards()
    {
        var time = new FakeTimeProvider();
        var cpu = TimeSpan.Zero;
        var meter = new ProcessMeter(() => (cpu += TimeSpan.FromMilliseconds(1), 0, 0), time, 1);
        meter.Read();

        // La mesure et l'horodatage sont pris ensemble : le temps CPU lu ne recule jamais d'une lecture à l'autre.
        Parallel.For(0, 2000, _ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(1));
            var percent = meter.Read().CpuPercent;
            Assert.InRange(percent, 0, 100);
        });
    }
}
