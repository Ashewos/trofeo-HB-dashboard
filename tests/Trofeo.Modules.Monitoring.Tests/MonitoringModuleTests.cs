using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;
using Trofeo.Sensors;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Monitoring.Tests;

public class MonitoringModuleTests
{
    private sealed class FakeFeed : ISensorFeed
    {
        public SensorSnapshot? Latest { get; set; }
        public int Acquired { get; private set; }
        public int Released { get; private set; }
        public int Subscribers => Updated?.GetInvocationList().Length ?? 0;
        public event EventHandler? Updated;

        public IDisposable Acquire()
        {
            Acquired++;
            return new Ticket(this);
        }

        public void Publish(SensorSnapshot snapshot)
        {
            Latest = snapshot;
            Updated?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Ticket(FakeFeed owner) : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                owner.Released++;
            }
        }
    }

    private static SensorSnapshot Full(bool cpuTemp = true)
    {
        var values = new Dictionary<string, double>
        {
            [SensorKeys.CpuLoad] = 34, [SensorKeys.CpuClock] = 5449, [SensorKeys.CpuPower] = 125,
            [SensorKeys.GpuLoad] = 61, [SensorKeys.GpuTemp] = 64, [SensorKeys.GpuClock] = 2805, [SensorKeys.GpuPower] = 180,
            [SensorKeys.GpuVramUsed] = 5222, [SensorKeys.GpuVramTotal] = 12288, [SensorKeys.GpuFan] = 1450,
            [SensorKeys.RamUsed] = 18.24, [SensorKeys.RamTotal] = 31.8,
            [SensorKeys.DiskUsed] = 63, [SensorKeys.DiskTemp] = 38,
        };
        if (cpuTemp) values[SensorKeys.CpuTemp] = 52;
        return new SensorSnapshot("Intel(R) Core(TM) Ultra 7 265K", "NVIDIA GeForce RTX 5070", values, Elevated: true);
    }

    private static MonitoringModule Create(FakeFeed feed, TimeProvider time, string json = "{}")
    {
        using var doc = JsonDocument.Parse(json);
        var context = new ModuleContext(
            Guid.NewGuid(), doc.RootElement.Clone(), time, new FakeModuleLog(), new FakeSecretStore());
        return (MonitoringModule)new MonitoringFactory(feed).Create(context);
    }

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Descriptor_MatchesBrief()
    {
        var d = new MonitoringFactory(new FakeFeed()).Descriptor;

        Assert.Equal("monitoring", d.TypeId);
        Assert.Equal("Monitoring PC", d.DisplayName);
        Assert.Equal(new GridSize(3, 2), d.MinSize);
        Assert.Equal(new GridSize(4, 2), d.Presets[SizePreset.Compact]);
        Assert.Equal(new GridSize(6, 2), d.Presets[SizePreset.Ideal]);
        Assert.Equal(new GridSize(6, 4), d.Presets[SizePreset.Large]);
    }

    [Fact]
    public void Title_BeforeAnySnapshot_IsMonitoring()
    {
        using var module = Create(new FakeFeed(), Clock());

        Assert.Equal("Monitoring", module.CurrentTitle());
    }

    [Fact]
    public void Title_Default_IsShortHardwareName()
    {
        var feed = new FakeFeed { Latest = Full() };
        using var module = Create(feed, Clock());

        Assert.Equal("Core Ultra 7 265K · RTX 5070", module.CurrentTitle());
    }

    [Fact]
    public void Title_CustomTitle_WinsOverHardwareName()
    {
        var feed = new FakeFeed { Latest = Full() };
        using var module = Create(feed, Clock(), """{"title":"Mon PC"}""");

        Assert.Equal("Mon PC", module.CurrentTitle());
    }

    [Fact]
    public void Title_NoCpuTemp_ShowsDriverHint()
    {
        var feed = new FakeFeed { Latest = Full(cpuTemp: false) };
        using var module = Create(feed, Clock());

        Assert.Equal("Temp. CPU : installez le pilote (Paramètres)", module.CurrentTitle());
    }

    [Fact]
    public void Title_NoCpuTemp_HintReplacesCustomTitleToo()
    {
        var feed = new FakeFeed { Latest = Full(cpuTemp: false) };
        using var module = Create(feed, Clock(), """{"title":"Mon PC"}""");

        Assert.Equal("Temp. CPU : installez le pilote (Paramètres)", module.CurrentTitle());
    }

    [Fact]
    public void Title_NoCpuTemp_NoHintWithoutSnapshotOrCpuRow()
    {
        using var noSnapshot = Create(new FakeFeed(), Clock());
        Assert.Equal("Monitoring", noSnapshot.CurrentTitle());

        var feed = new FakeFeed { Latest = Full(cpuTemp: false) };
        using var noCpuRow = Create(feed, Clock(), """{"rows":["gpu","ram"]}""");
        Assert.Equal("Core Ultra 7 265K · RTX 5070", noCpuRow.CurrentTitle());
    }

    [Fact]
    public void Title_NoCpuTemp_NoHintWhenCpuRowIsBeyondCapacity()
    {
        var feed = new FakeFeed { Latest = Full(cpuTemp: false) };
        using var module = Create(feed, Clock(), """{"rows":["gpu","ram","vram","cpu"]}""");

        Assert.Equal("Core Ultra 7 265K · RTX 5070", module.CurrentTitle(drawnRows: 3));
        Assert.Equal("Temp. CPU : installez le pilote (Paramètres)", module.CurrentTitle(drawnRows: 4));
    }

    [Fact]
    public async Task Status_NoSnapshotFor5s_GoesOffline_ThenBackOnline()
    {
        var time = Clock();
        var feed = new FakeFeed();
        using var module = Create(feed, time);
        var changes = 0;
        module.StatusChanged += (_, _) => changes++;
        await module.StartAsync(CancellationToken.None);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.Equal(0, changes);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        Assert.Equal(1, changes);

        time.Advance(TimeSpan.FromSeconds(10)); // reste hors ligne : pas de nouvel évènement
        Assert.Equal(1, changes);

        feed.Publish(Full());
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.Equal(2, changes);

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task Status_RegularSnapshots_KeepModuleOnline()
    {
        var time = Clock();
        var feed = new FakeFeed();
        using var module = Create(feed, time);
        var changes = 0;
        module.StatusChanged += (_, _) => changes++;
        await module.StartAsync(CancellationToken.None);

        for (var i = 0; i < 30; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            feed.Publish(Full());
        }

        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Updated_RaisesInvalidated_OncePerSnapshot()
    {
        var feed = new FakeFeed();
        using var module = Create(feed, Clock());
        var invalidated = 0;
        module.Invalidated += (_, _) => invalidated++;
        await module.StartAsync(CancellationToken.None);

        feed.Publish(Full());
        feed.Publish(Full());

        Assert.Equal(2, invalidated);
    }

    [Fact]
    public async Task StartAndDispose_AcquireAndReleaseTheFeed_Once()
    {
        var feed = new FakeFeed();
        var module = Create(feed, Clock());
        Assert.Equal(0, feed.Acquired);

        await module.StartAsync(CancellationToken.None);
        await module.StartAsync(CancellationToken.None);
        Assert.Equal(1, feed.Acquired);
        Assert.Equal(1, feed.Subscribers);

        module.Dispose();
        module.Dispose();
        Assert.Equal(1, feed.Released);
        Assert.Equal(0, feed.Subscribers);
    }

    [Fact]
    public async Task Dispose_StopsHealthTimerAndEvents()
    {
        var time = Clock();
        var feed = new FakeFeed();
        var module = Create(feed, time);
        var raised = 0;
        module.Invalidated += (_, _) => raised++;
        module.StatusChanged += (_, _) => raised++;
        await module.StartAsync(CancellationToken.None);
        module.Dispose();

        time.Advance(TimeSpan.FromMinutes(1));
        feed.Publish(Full());

        Assert.Equal(0, raised);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
    }

    [Fact]
    public async Task Dispose_BeforeStart_NeverAcquires()
    {
        var feed = new FakeFeed();
        var module = Create(feed, Clock());
        module.Dispose();

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(0, feed.Acquired);
    }

    // --- Rendu ---

    private static int CountDifferingPixels(SKBitmap bitmap, SKColor background, SKRectI exclude)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == background) continue;
                if (x >= exclude.Left && x < exclude.Right && y >= exclude.Top && y < exclude.Bottom) continue;
                count++;
            }
        }
        return count;
    }

    private static int RenderAndCountOutside(MonitoringModule module, SKRectI rect, float scale, Theme theme)
    {
        const int margin = 30;
        var bg = theme.Background;
        using var bitmap = new SKBitmap(GridGeometry.Width + 2 * margin, GridGeometry.Height + 2 * margin);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(bg);

        var bounds = new SKRect(rect.Left + margin, rect.Top + margin, rect.Right + margin, rect.Bottom + margin);
        module.Render(canvas, new ModuleRenderContext(
            bounds, new GridSize(1, 1), theme, scale, new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));

        var inside = new SKRectI((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom);
        var outside = CountDifferingPixels(bitmap, bg, inside);

        // Le module a bien dessiné quelque chose dans son rectangle.
        var drawnInside = 0;
        for (var y = inside.Top; y < inside.Bottom; y++)
            for (var x = inside.Left; x < inside.Right; x++)
                if (bitmap.GetPixel(x, y) != bg) drawnInside++;
        Assert.True(drawnInside > 500, $"seulement {drawnInside} pixels dessinés");

        return outside;
    }

    public static TheoryData<int, int, int, int, float> Layouts()
    {
        var data = new TheoryData<int, int, int, int, float>();
        foreach (var scale in new[] { 0.85f, 1.0f, 1.2f })
        {
            data.Add(8, 0, 4, 2, scale);   // Compact
            data.Add(5, 1, 6, 2, scale);   // Idéal
            data.Add(5, 0, 6, 4, scale);   // Large
            data.Add(0, 2, 3, 2, scale);   // taille minimale
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Render_AllRowsFitInsideBounds(int col, int row, int cols, int rows, float scale)
    {
        var feed = new FakeFeed { Latest = Full() };
        using var module = Create(feed, Clock(),
            """{"title":"Un titre personnalisé vraiment très long pour déborder","rows":["cpu","gpu","ram","vram","clock","power","fan","disk"]}""");
        await module.StartAsync(CancellationToken.None);

        var rect = GridGeometry.ModuleRect(col, row, cols, rows);

        Assert.Equal(0, RenderAndCountOutside(module, rect, scale, Themes.Classique));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Render_WithDriverHintAndMissingValues_StaysInsideBounds(int col, int row, int cols, int rows, float scale)
    {
        var feed = new FakeFeed { Latest = Full(cpuTemp: false) };
        using var module = Create(feed, Clock(), """{"rows":["cpu","fan","clock","power","disk","vram","ram","gpu"]}""");
        await module.StartAsync(CancellationToken.None);
        var empty = new SensorSnapshot(null, null, new Dictionary<string, double>(), Elevated: false);
        var rect = GridGeometry.ModuleRect(col, row, cols, rows);

        Assert.Equal(0, RenderAndCountOutside(module, rect, scale, Themes.Classique));

        feed.Latest = empty;
        Assert.Equal(0, RenderAndCountOutside(module, rect, scale, Themes.Classique));
        feed.Latest = null;
        Assert.Equal(0, RenderAndCountOutside(module, rect, scale, Themes.Classique));
    }

    [Fact]
    public async Task Render_DrawsAtMostCapacityRows()
    {
        // 4×2 à l'échelle 1 : 3 lignes, et ce sont les trois premières des réglages.
        var feed = new FakeFeed { Latest = Full() };
        using var module = Create(feed, Clock(), """{"rows":["cpu","gpu","ram","vram","fan"]}""");
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);
        var bounds = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

        Assert.Equal(3, MonitoringRows.Capacity(bounds, 1f));
        Assert.Equal(3, module.VisibleRows(bounds, 1f).Count);
        Assert.Equal(new[] { "cpu", "gpu", "ram" }, module.VisibleRows(bounds, 1f));
    }
}
