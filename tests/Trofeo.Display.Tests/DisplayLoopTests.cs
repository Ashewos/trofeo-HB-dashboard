using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Device;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Display.Tests;

public sealed class DisplayLoopTests
{
    private static readonly Guid InstanceId = new("11111111-1111-1111-1111-111111111111");

    private sealed class FakeModuleFactory(string typeId, GridSize minSize, Func<IModule> make) : IModuleFactory
    {
        public ModuleDescriptor Descriptor { get; } = new(typeId, typeId, minSize, new Dictionary<SizePreset, GridSize>());

        public IModule Create(JsonElement settings, TimeProvider time) => make();
    }

    private sealed class Rig : IDisposable
    {
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
        public FakeModule Module { get; }
        public FakeUsbTransportFactory Usb { get; } = new();
        public ScreenDevice Screen { get; }
        public DisplayLoop Loop { get; }
        public FrameScheduler Scheduler { get; }

        public Rig(FakeModule? module = null, int cols = 4, int rows = 2, string typeId = "fake")
        {
            Module = module ?? new FakeModule();
            Screen = new ScreenDevice(Usb, Time);
            Scheduler = new FrameScheduler(Time);
            var registry = new ModuleRegistry([new FakeModuleFactory("fake", new GridSize(2, 1), () => Module)]);
            var layout = new DashboardLayout(180,
            [
                new ModulePlacement(InstanceId, typeId, 0, 0, cols, rows, TextSize.Normal,
                    JsonDocument.Parse("{}").RootElement.Clone()),
            ]);
            Loop = new DisplayLoop(layout, registry, Screen, Scheduler, Time);
        }

        /// <summary>Un écran sain, prêt à valider <paramref name="acks"/> images.</summary>
        public FakeUsbTransport AddHealthyScreen(int acks = 20)
        {
            var transport = new FakeUsbTransport().EnqueueReadTimeout().EnqueueHandshakeReply();
            for (var i = 0; i < acks; i++) transport.EnqueueAck();
            Usb.EnqueueTransport(transport);
            return transport;
        }

        public void Dispose()
        {
            Loop.Dispose();
            Screen.Dispose();
        }
    }

    [Fact]
    public void Tick_FirstTick_ConnectsAndSendsFirstFrame()
    {
        using var rig = new Rig();
        rig.AddHealthyScreen();

        rig.Loop.Tick();

        Assert.Equal(ScreenState.Ready, rig.Screen.State);
        Assert.Equal(1, rig.Loop.FramesSent);
        Assert.Single(rig.Module.RenderCalls);
    }

    [Fact]
    public void Tick_Idle_SendsNothing()
    {
        using var rig = new Rig();
        var transport = rig.AddHealthyScreen();
        rig.Loop.Tick();
        var writesAfterFirstFrame = transport.Writes.Count();

        for (var i = 0; i < 500; i++) // 5 s simulées à 10 ms
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
            rig.Loop.Tick();
        }

        Assert.Equal(1, rig.Loop.FramesSent);
        Assert.Equal(writesAfterFirstFrame, transport.Writes.Count());
        Assert.Single(rig.Module.RenderCalls);
    }

    [Fact]
    public void Tick_Invalidated_SendsOneFrame()
    {
        using var rig = new Rig();
        rig.AddHealthyScreen();
        rig.Loop.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(1));

        rig.Module.RaiseInvalidated();
        rig.Loop.Tick();
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick(); // la demande est consommée : rien de plus

        Assert.Equal(2, rig.Loop.FramesSent);
    }

    [Fact]
    public void Tick_Animating_IsCappedAt12Fps()
    {
        using var rig = new Rig(new FakeModule { IsAnimating = true });
        rig.AddHealthyScreen(acks: 100);

        for (var i = 0; i < 100; i++) // 1 s simulée à 10 ms
        {
            rig.Loop.Tick();
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
        }

        Assert.InRange(rig.Loop.FramesSent, 11, 13);
    }

    [Fact]
    public void Tick_Disconnected_RetriesEvery2s()
    {
        using var rig = new Rig(); // aucun écran : TryOpen renvoie null

        rig.Loop.Tick();
        Assert.Equal(1, rig.Usb.OpenCount); // tout de suite au premier passage

        for (var i = 0; i < 199; i++) // jusqu'à 1,99 s
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
            rig.Loop.Tick();
        }

        Assert.Equal(1, rig.Usb.OpenCount);

        rig.Time.Advance(TimeSpan.FromMilliseconds(10)); // 2,00 s
        rig.Loop.Tick();
        Assert.Equal(2, rig.Usb.OpenCount);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();
        Assert.Equal(2, rig.Usb.OpenCount);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Loop.Tick();
        Assert.Equal(3, rig.Usb.OpenCount);
        Assert.Equal(0, rig.Loop.FramesSent);
    }

    [Fact]
    public void Tick_SendFails_ReconnectsAndResendsWithoutNewInvalidation()
    {
        using var rig = new Rig();
        var first = rig.AddHealthyScreen(acks: 1);
        rig.Loop.Tick();
        Assert.Equal(1, rig.Loop.FramesSent);

        first.FailNextWritesWithIOException(1); // débranchement pendant l'envoi suivant
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Module.RaiseInvalidated();
        rig.Loop.Tick();
        Assert.Equal(ScreenState.Disconnected, rig.Screen.State);
        Assert.Equal(1, rig.Loop.FramesSent);

        var second = rig.AddHealthyScreen();
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        rig.Loop.Tick();

        Assert.Equal(ScreenState.Ready, rig.Screen.State);
        Assert.True(rig.Loop.FramesSent >= 2);
        Assert.NotEmpty(second.Writes);
    }

    [Fact]
    public void Constructor_UnknownType_IsSkippedAndOthersStillRender()
    {
        using var rig = new Rig(typeId: "inconnu");
        rig.AddHealthyScreen();

        rig.Loop.Tick();

        Assert.Equal(1, rig.Loop.FramesSent);
        Assert.Empty(rig.Module.RenderCalls);
    }

    [Fact]
    public void Constructor_BelowMinSize_IsSkipped()
    {
        using var rig = new Rig(cols: 1, rows: 1); // le minimum du faux module est 2 × 1
        rig.AddHealthyScreen();

        rig.Loop.Tick();

        Assert.Empty(rig.Module.RenderCalls);
    }

    [Fact]
    public void Dispose_UnsubscribesInvalidated()
    {
        var rig = new Rig();
        rig.AddHealthyScreen();
        rig.Loop.Tick();
        rig.Loop.Dispose();

        rig.Module.RaiseInvalidated(); // toléré après Dispose
        rig.Time.Advance(TimeSpan.FromSeconds(1)); // au-delà des 12 images/s : seule la désinscription empêche le redessin

        Assert.False(rig.Scheduler.ShouldRender(false));
        rig.Screen.Dispose();
    }
}

public sealed class ModuleRegistryTests
{
    private static ModulePlacement Placement(string typeId) => new(
        Guid.NewGuid(), typeId, 0, 0, 4, 2, TextSize.Normal, JsonDocument.Parse("{}").RootElement.Clone());

    [Fact]
    public void Create_KnownType_ReturnsModule()
    {
        var registry = new ModuleRegistry([new Trofeo.Modules.Clock.ClockFactory()]);

        using var module = registry.Create(Placement("clock"), TimeProvider.System);

        Assert.NotNull(module);
        Assert.True(registry.Descriptors.ContainsKey("clock"));
    }

    [Fact]
    public void Create_UnknownType_ReturnsNull()
    {
        var registry = new ModuleRegistry([new Trofeo.Modules.Clock.ClockFactory()]);

        Assert.Null(registry.Create(Placement("meteo"), TimeProvider.System));
    }
}

public sealed class TrayIconTests
{
    [Theory]
    [InlineData(ScreenState.Ready, "Trofeo dashboard — écran : prêt")]
    [InlineData(ScreenState.Disconnected, "Trofeo dashboard — écran : non branché")]
    [InlineData(ScreenState.Recovering, "Trofeo dashboard — écran : reprise…")]
    public void Tooltip_MapsScreenState(ScreenState state, string expected)
    {
        Assert.Equal(expected, TrayIcon.Tooltip(state));
    }
}
