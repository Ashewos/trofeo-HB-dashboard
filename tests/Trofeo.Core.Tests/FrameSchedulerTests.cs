using Microsoft.Extensions.Time.Testing;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public class FrameSchedulerTests
{
    /// <summary>
    /// Échantillonne ShouldRender à t = 0, 10, 20, … ms sur la fenêtre semi-ouverte [0 ms, 1000 ms)
    /// (100 échantillons) et compte les envois.
    /// </summary>
    private static int CountRendersOverOneSecond(FakeTimeProvider time, FrameScheduler scheduler)
    {
        int renders = 0;
        for (int i = 0; i < 100; i++)
        {
            if (scheduler.ShouldRender(anyAnimating: true))
            {
                renders++;
                scheduler.OnRendered();
            }
            time.Advance(TimeSpan.FromMilliseconds(10));
        }
        return renders;
    }

    [Fact]
    public void MarkDirty_SetsSignal()
    {
        using var scheduler = new FrameScheduler(new FakeTimeProvider());
        Assert.False(scheduler.Signal.WaitOne(0));

        scheduler.MarkDirty();

        Assert.True(scheduler.IsDirty);
        Assert.True(scheduler.Signal.WaitOne(0));
        Assert.False(scheduler.Signal.WaitOne(0)); // se réarme seul
    }

    [Fact]
    public void Wake_SetsSignal_WithoutMarkingDirty()
    {
        using var scheduler = new FrameScheduler(new FakeTimeProvider());

        scheduler.Wake();

        Assert.False(scheduler.IsDirty);
        Assert.True(scheduler.Signal.WaitOne(0));
    }

    [Fact]
    public void MarkDirty_AfterDispose_DoesNotThrow()
    {
        var scheduler = new FrameScheduler(new FakeTimeProvider());
        scheduler.Dispose();

        scheduler.MarkDirty();
        scheduler.Wake();
    }

    [Fact]
    public void IsDirty_DoesNotConsume_ShouldRenderDoes()
    {
        var scheduler = new FrameScheduler(new FakeTimeProvider());
        scheduler.MarkDirty();

        Assert.True(scheduler.IsDirty);
        Assert.True(scheduler.IsDirty);
        Assert.True(scheduler.ShouldRender(anyAnimating: false));
        Assert.False(scheduler.IsDirty);
    }

    [Fact]
    public void UntilNextSlot_BeforeAnyRender_IsZero()
    {
        var scheduler = new FrameScheduler(new FakeTimeProvider());

        Assert.Equal(TimeSpan.Zero, scheduler.UntilNextSlot());
    }

    [Fact]
    public void UntilNextSlot_AfterRender_IsRemainingInterval()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time);
        scheduler.OnRendered();
        Assert.Equal(scheduler.MinInterval, scheduler.UntilNextSlot());

        time.Advance(TimeSpan.FromMilliseconds(30));
        Assert.Equal(scheduler.MinInterval - TimeSpan.FromMilliseconds(30), scheduler.UntilNextSlot());

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, scheduler.UntilNextSlot());
    }

    [Fact]
    public void Idle_NeverRenders()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time);

        Assert.False(scheduler.ShouldRender(anyAnimating: false));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(scheduler.ShouldRender(anyAnimating: false));
    }

    [Fact]
    public void Dirty_RendersOnce()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time);

        scheduler.MarkDirty();
        Assert.True(scheduler.ShouldRender(anyAnimating: false));
        scheduler.OnRendered();

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(scheduler.ShouldRender(anyAnimating: false));
    }

    [Fact]
    public void Dirty_WaitsForMinInterval()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time);

        scheduler.MarkDirty();
        Assert.True(scheduler.ShouldRender(anyAnimating: false));
        scheduler.OnRendered();

        scheduler.MarkDirty();
        time.Advance(TimeSpan.FromMilliseconds(80));
        Assert.False(scheduler.ShouldRender(anyAnimating: false));

        time.Advance(TimeSpan.FromMilliseconds(4));
        Assert.True(scheduler.ShouldRender(anyAnimating: false));
    }

    [Fact]
    public void MarkDirty_DuringRender_IsNotLost()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time);

        scheduler.MarkDirty();
        Assert.True(scheduler.ShouldRender(anyAnimating: false));
        scheduler.MarkDirty(); // un module demande un nouveau dessin pendant le rendu
        scheduler.OnRendered();

        time.Advance(scheduler.MinInterval);
        Assert.True(scheduler.ShouldRender(anyAnimating: false));
    }

    [Fact]
    public void Animating_CappedAt12fps()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time);

        Assert.Equal(12, CountRendersOverOneSecond(time, scheduler));
    }

    [Fact]
    public void MaxFps_Configurable()
    {
        var time = new FakeTimeProvider();
        var scheduler = new FrameScheduler(time, maxFps: 6);

        Assert.Equal(6, CountRendersOverOneSecond(time, scheduler));
    }

    [Fact]
    public void MinInterval_IsOneOverMaxFps()
    {
        var scheduler = new FrameScheduler(new FakeTimeProvider());

        Assert.Equal(1000.0 / 12, scheduler.MinInterval.TotalMilliseconds, precision: 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidMaxFps_Throws(double maxFps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameScheduler(new FakeTimeProvider(), maxFps));
    }
}
