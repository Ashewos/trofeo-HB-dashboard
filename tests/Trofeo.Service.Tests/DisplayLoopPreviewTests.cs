using Trofeo.Core.Layout;
using Trofeo.Device;
using Trofeo.ModuleKit;
using Trofeo.Control;
using static Trofeo.Display.Tests.LoopRig;

namespace Trofeo.Display.Tests;

public sealed class DisplayLoopPreviewTests
{
    private static List<PreviewFrame> Collect(LoopRig rig, int maxFps, out IDisposable subscription)
    {
        var frames = new List<PreviewFrame>();
        subscription = rig.Loop.SubscribePreview(maxFps, f => { lock (frames) frames.Add(f); });
        return frames;
    }

    [Fact]
    public void Preview_ScreenReady_PublishesSentJpeg()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;

        rig.Loop.Tick();

        var frame = Assert.Single(frames);
        Assert.Equal(180, frame.Rotation);
        Assert.Equal(rig.Screen.LastFrame, frame.Jpeg); // octets identiques à ceux envoyés
        Assert.Equal(0xFF, frame.Jpeg[0]);
        Assert.Equal(1, rig.Loop.FramesSent);
        Assert.False(rig.Loop.Status.Emulated);
    }

    [Fact]
    public void Preview_KeepAlivesAreNotPublished()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;
        rig.Loop.Tick();

        for (var i = 0; i < 300; i++)
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
            rig.Loop.Tick();
        }

        Assert.True(rig.Loop.KeepAlivesSent >= 2);
        Assert.Single(frames);
    }

    [Fact]
    public void Preview_NoScreen_RendersEmulated()
    {
        using var rig = new LoopRig();
        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;

        rig.Loop.Tick();

        var frame = Assert.Single(frames);
        Assert.Equal(180, frame.Rotation);
        Assert.Equal(0xFF, frame.Jpeg[0]);
        Assert.True(rig.Loop.Status.Emulated);
        Assert.Equal(0, rig.Loop.FramesSent); // rien n'est parti vers l'écran
        Assert.Null(rig.Screen.LastFrame);
        Assert.Single(rig.Created[0].Rects);
    }

    [Fact]
    public void Preview_NoScreen_KeepsRetryingToConnectEvery2Seconds()
    {
        using var rig = new LoopRig();
        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;
        rig.Loop.Tick();
        Assert.Equal(1, rig.Usb.OpenCount);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();
        Assert.Equal(1, rig.Usb.OpenCount);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();
        Assert.Equal(2, rig.Usb.OpenCount);

        // L'écran apparaît : la boucle passe en mode réel et envoie.
        rig.AddHealthyScreen();
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        rig.Loop.Tick();

        Assert.Equal(ScreenState.Ready, rig.Screen.State);
        Assert.False(rig.Loop.Status.Emulated);
        Assert.Equal(1, rig.Loop.FramesSent);
        Assert.Equal(frames[^1].Jpeg, rig.Screen.LastFrame);
    }

    [Fact]
    public void Preview_NoScreen_AnimatedBackgroundAdvances()
    {
        var background = new StepBackground();
        using var rig = new LoopRig(background: background);
        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;

        for (var i = 0; i < 5; i++)
        {
            rig.Loop.Tick();
            rig.Time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(background.AdvanceTimes.Count >= 4);
        Assert.True(frames.Count >= 4);
    }

    [Fact]
    public void NoScreen_NoSubscriber_RendersNothing()
    {
        using var rig = new LoopRig();

        rig.Loop.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(3));
        rig.Loop.Tick();

        Assert.Empty(rig.Created[0].Rects);
        Assert.False(rig.Loop.Status.Emulated);
        Assert.Equal(0, rig.Loop.FramesSent);
    }

    [Fact]
    public void Preview_RespectsSubscriberMaxFps()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen(acks: 400);
        rig.Created[0].IsAnimating = true; // une image à chaque occasion : 12 par seconde au plus
        var slow = Collect(rig, 4, out var slowSub);
        var fast = Collect(rig, 12, out var fastSub);
        using var _1 = slowSub;
        using var _2 = fastSub;

        for (var i = 0; i < 100; i++) // 1 s simulée, [0 ; 990 ms]
        {
            rig.Loop.Tick();
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
        }

        Assert.InRange(slow.Count, 3, 4);
        Assert.InRange(fast.Count, 10, 12);
    }

    [Fact]
    public void Preview_Throttled_LatestFrameIsStillDelivered()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        var frames = Collect(rig, 1, out var sub);
        using var _ = sub;
        rig.Loop.Tick();
        Assert.Single(frames);

        rig.Time.Advance(TimeSpan.FromMilliseconds(200));
        rig.Scheduler.MarkDirty(); // un changement arrive trop tôt pour cet abonné
        rig.Loop.Tick();
        Assert.Single(frames);

        rig.Time.Advance(TimeSpan.FromMilliseconds(800)); // sans autre demande d'image, l'abonné reçoit la dernière
        rig.Loop.Tick();
        Assert.Equal(2, frames.Count);
        Assert.True(rig.Loop.Tick() > TimeSpan.Zero);
    }

    [Fact]
    public void Preview_Throttled_TickDelayAccountsForPendingFrame()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        var frames = Collect(rig, 1, out var sub);
        using var _ = sub;
        rig.Loop.Tick();
        rig.Time.Advance(TimeSpan.FromMilliseconds(200));
        rig.Scheduler.MarkDirty();

        var delay = rig.Loop.Tick();

        Assert.Single(frames);
        Assert.InRange(delay.TotalMilliseconds, 1, 800); // se réveille pour livrer l'image en attente, pas une seconde plus tard
    }

    [Fact]
    public void Preview_NewSubscriber_GetsAFreshFrame()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        rig.Loop.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();

        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;
        rig.Loop.Tick();

        Assert.Single(frames);
    }

    [Fact]
    public void Preview_Unsubscribe_StopsEmulatedRendering()
    {
        using var rig = new LoopRig();
        var frames = Collect(rig, 12, out var sub);
        rig.Loop.Tick();
        Assert.Single(frames);

        sub.Dispose();
        rig.Scheduler.MarkDirty();
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();

        Assert.Single(frames);
        Assert.Single(rig.Created[0].Rects);
        Assert.False(rig.Loop.Status.Emulated);
    }

    [Fact]
    public void Preview_Unsubscribe_ScreenReady_NoMorePublishing()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        var frames = Collect(rig, 12, out var sub);
        rig.Loop.Tick();
        sub.Dispose();
        sub.Dispose(); // idempotent

        rig.Scheduler.MarkDirty();
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();

        Assert.Single(frames);
        Assert.Equal(2, rig.Loop.FramesSent);
    }

    [Fact]
    public void Preview_ThrowingSubscriber_DoesNotBreakLoopOrOthers()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen();
        using var bad = rig.Loop.SubscribePreview(12, _ => throw new InvalidOperationException("abonné cassé"));
        var frames = Collect(rig, 12, out var sub);
        using var _ = sub;

        rig.Loop.Tick();

        Assert.Single(frames);
        Assert.Equal(1, rig.Loop.FramesSent);
    }

    [Fact]
    public void Preview_MaxFps_IsBounded()
    {
        using var rig = new LoopRig();
        rig.AddHealthyScreen(acks: 400);
        rig.Created[0].IsAnimating = true;
        var frames = Collect(rig, 1000, out var greedy); // plafonné à 12
        var slow = Collect(rig, 0, out var floor);       // relevé à 1
        using var _1 = greedy;
        using var _2 = floor;

        for (var i = 0; i < 300; i++)
        {
            rig.Loop.Tick();
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
        }

        Assert.InRange(frames.Count, 30, 37);
        Assert.InRange(slow.Count, 3, 4);
    }

    [Fact]
    public void Preview_AfterDispose_SubscribeIsInertNoThrow()
    {
        var rig = new LoopRig();
        rig.Dispose();

        using var sub = rig.Loop.SubscribePreview(12, _ => { });
    }
}
