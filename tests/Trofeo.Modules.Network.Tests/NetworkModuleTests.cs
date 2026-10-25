using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Network.Tests;

public class NetworkModuleTests
{
    private sealed class FakeSource : IThroughputSource
    {
        public Func<Task<SourceReading>> OnRead { get; set; } =
            () => Task.FromResult(new SourceReading(new ThroughputSample(1_000_000, 250_000), new ModuleStatus(ModuleHealth.Online)));

        public string Title => "Fake";
        public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);
        public int Reads { get; private set; }
        public int Disposed { get; private set; }

        public Task<SourceReading> ReadAsync(CancellationToken ct)
        {
            Reads++;
            return OnRead();
        }

        public void Dispose() => Disposed++;
    }

    private sealed record Rig(NetworkModule Module, FakeSource Source, FakeTimeProvider Time, FakeModuleLog Log) : IDisposable
    {
        public void Dispose() => Module.Dispose();
    }

    private static Rig Create(FakeSource? source = null, string json = "{}")
    {
        source ??= new FakeSource();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        var log = new FakeModuleLog();
        using var doc = JsonDocument.Parse(json);
        var context = new ModuleContext(Guid.NewGuid(), doc.RootElement.Clone(), time, log, new FakeSecretStore());
        var module = (NetworkModule)new NetworkFactory((_, _) => source).Create(context);
        return new Rig(module, source, time, log);
    }

    private static SourceReading Reading(double down, double up) =>
        new(new ThroughputSample(down, up), new ModuleStatus(ModuleHealth.Online));

    [Fact]
    public void Descriptor_MatchesBrief()
    {
        var d = new NetworkFactory((_, _) => new FakeSource()).Descriptor;

        Assert.Equal("network", d.TypeId);
        Assert.Equal("Réseau", d.DisplayName);
        Assert.Equal(new GridSize(3, 1), d.MinSize);
        Assert.Equal(new GridSize(3, 1), d.Presets[SizePreset.Compact]);
        Assert.Equal(new GridSize(4, 2), d.Presets[SizePreset.Ideal]);
        Assert.Equal(new GridSize(6, 2), d.Presets[SizePreset.Large]);
    }

    [Fact]
    public void Factory_PassesParsedSettingsAndContextToCreateSource()
    {
        NetworkSettings? seen = null;
        ModuleContext? seenContext = null;
        var factory = new NetworkFactory((settings, ctx) =>
        {
            seen = settings;
            seenContext = ctx;
            return new FakeSource();
        });
        using var doc = JsonDocument.Parse("""{"source":"unifi","unifi":{"host":"10.0.0.1"}}""");
        var context = new ModuleContext(Guid.NewGuid(), doc.RootElement.Clone(), TimeProvider.System, new FakeModuleLog(), new FakeSecretStore());

        using var module = factory.Create(context);

        Assert.Equal(new NetworkSettings(NetworkSourceKind.UniFi, "10.0.0.1"), seen);
        Assert.Same(context, seenContext);
    }

    [Fact]
    public async Task CreateDefault_BuildsAWorkingModuleForEverySource()
    {
        foreach (var source in new[] { "pc", "freebox", "unifi" })
        {
            using var doc = JsonDocument.Parse($$"""{"source":"{{source}}"}""");
            var context = new ModuleContext(Guid.NewGuid(), doc.RootElement.Clone(), TimeProvider.System, new FakeModuleLog(), new FakeSecretStore());

            using var module = NetworkFactory.CreateDefault().Create(context);
            await module.StartAsync(CancellationToken.None);

            Assert.Equal(ModuleHealth.Online, module.Status.Health);
        }
    }

    [Fact]
    public async Task Start_ReadsOnce_ThenEveryInterval()
    {
        var rig = Create(new FakeSource { Interval = TimeSpan.FromSeconds(2) });
        await rig.Module.StartAsync(CancellationToken.None);
        Assert.Equal(1, rig.Source.Reads);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, rig.Source.Reads);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, rig.Source.Reads);

        rig.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(4, rig.Source.Reads);
    }

    [Fact]
    public async Task Start_Twice_StartsOnce()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        await rig.Module.StartAsync(CancellationToken.None);

        Assert.Equal(1, rig.Source.Reads);
    }

    [Fact]
    public async Task Reads_NeverOverlap()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        var gate = new TaskCompletionSource<SourceReading>();
        rig.Source.OnRead = () => gate.Task;

        rig.Time.Advance(TimeSpan.FromSeconds(1)); // lecture lancée, jamais terminée
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, rig.Source.Reads); // celle du démarrage et une seule en cours

        gate.SetResult(Reading(5, 5));
        Assert.True(SpinWait.SpinUntil(() => rig.Module.HistorySnapshot().Count == 2, TimeSpan.FromSeconds(5)));
        rig.Source.OnRead = () => Task.FromResult(Reading(6, 6));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, rig.Source.Reads);
    }

    [Fact]
    public async Task Read_AddsOneHistoryEntryPerSample_AndNoneWithoutSample()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        Assert.Single(rig.Module.HistorySnapshot());

        rig.Source.OnRead = () => Task.FromResult(new SourceReading(null, new ModuleStatus(ModuleHealth.Online)));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(rig.Module.HistorySnapshot());

        rig.Source.OnRead = () => Task.FromResult(Reading(7, 3));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        var history = rig.Module.HistorySnapshot();
        Assert.Equal(2, history.Count);
        Assert.Equal(new ThroughputSample(7, 3), history[1].Sample);
        Assert.Equal(rig.Time.GetUtcNow(), history[1].At);
    }

    [Fact]
    public async Task History_KeepsThreeMinutes()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);

        for (var i = 0; i < 300; i++) rig.Time.Advance(TimeSpan.FromSeconds(1));

        var history = rig.Module.HistorySnapshot();
        Assert.Equal(181, history.Count);
        Assert.Equal(TimeSpan.FromMinutes(3), history[^1].At - history[0].At);
    }

    [Fact]
    public async Task Module_SourceThrows_GoesOfflineAndKeepsHistory()
    {
        var rig = Create();
        var changes = 0;
        rig.Module.StatusChanged += (_, _) => changes++;
        await rig.Module.StartAsync(CancellationToken.None);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, rig.Module.HistorySnapshot().Count);

        rig.Source.OnRead = () => throw new InvalidOperationException("la box ne répond pas");
        rig.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new ModuleStatus(ModuleHealth.Offline), rig.Module.Status);
        Assert.Equal(1, changes);
        Assert.Equal(2, rig.Module.HistorySnapshot().Count);
        Assert.Contains(rig.Log.Entries, e => e.StartsWith("Error") && e.Contains("la box ne répond pas"));

        // Un échec asynchrone est traité de même, et ne relève pas StatusChanged tant que l'état ne change pas.
        rig.Source.OnRead = () => Task.FromException<SourceReading>(new HttpRequestException("coupé"));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ModuleHealth.Offline, rig.Module.Status.Health);
        Assert.Equal(1, changes);

        rig.Source.OnRead = () => Task.FromResult(Reading(1, 1));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ModuleHealth.Online, rig.Module.Status.Health);
        Assert.Equal(2, changes);
        Assert.Equal(3, rig.Module.HistorySnapshot().Count);
    }

    [Fact]
    public async Task Module_FirstReadThrows_StartStillCompletesAndRetries()
    {
        var source = new FakeSource { OnRead = () => throw new InvalidOperationException("boom") };
        var rig = Create(source);

        await rig.Module.StartAsync(CancellationToken.None);

        Assert.Equal(ModuleHealth.Offline, rig.Module.Status.Health);
        source.OnRead = () => Task.FromResult(Reading(1, 1));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(ModuleHealth.Online, rig.Module.Status.Health);
    }

    [Fact]
    public async Task Status_PassesThroughSourceStatus_WithActionMessage()
    {
        var rig = Create();
        var changes = 0;
        rig.Module.StatusChanged += (_, _) => changes++;
        await rig.Module.StartAsync(CancellationToken.None);

        var waiting = new ModuleStatus(ModuleHealth.NotConfigured, "Appuyez sur la flèche de la box");
        rig.Source.OnRead = () => Task.FromResult(new SourceReading(null, waiting));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(waiting, rig.Module.Status);
        Assert.Equal(1, changes);

        rig.Time.Advance(TimeSpan.FromSeconds(1)); // même état : pas de nouvel évènement
        Assert.Equal(1, changes);

        var other = new ModuleStatus(ModuleHealth.NotConfigured, "Autre message");
        rig.Source.OnRead = () => Task.FromResult(new SourceReading(null, other));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(other, rig.Module.Status);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Invalidated_RaisedAfterEveryRead_EvenWithoutStatusChange()
    {
        var rig = Create();
        var invalidated = 0;
        rig.Module.Invalidated += (_, _) => invalidated++;
        await rig.Module.StartAsync(CancellationToken.None);
        Assert.Equal(1, invalidated);

        rig.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(4, invalidated);

        rig.Source.OnRead = () => throw new InvalidOperationException("boom");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(5, invalidated);
    }

    [Fact]
    public async Task Dispose_StopsReadingDisposesSourceAndIgnoresLateReads()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        var gate = new TaskCompletionSource<SourceReading>();
        rig.Source.OnRead = () => gate.Task;
        rig.Time.Advance(TimeSpan.FromSeconds(1)); // lecture en cours
        var raised = 0;
        rig.Module.Invalidated += (_, _) => raised++;
        rig.Module.StatusChanged += (_, _) => raised++;

        rig.Module.Dispose();
        rig.Module.Dispose();
        gate.SetResult(new SourceReading(new ThroughputSample(9, 9), new ModuleStatus(ModuleHealth.Offline)));
        await Task.Delay(100); // laisse à la lecture tardive le temps de se terminer
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, rig.Source.Disposed);
        Assert.Equal(2, rig.Source.Reads);
        Assert.Equal(0, raised);
        Assert.Equal(ModuleHealth.Online, rig.Module.Status.Health);
        Assert.Single(rig.Module.HistorySnapshot());
    }

    [Fact]
    public async Task Dispose_BeforeStart_NeverReads()
    {
        var rig = Create();
        rig.Module.Dispose();

        await rig.Module.StartAsync(CancellationToken.None);

        Assert.Equal(0, rig.Source.Reads);
        Assert.Equal(1, rig.Source.Disposed);
    }

    // --- Rendu ---

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Rig> CreateWithHistory(Func<int, SourceReading>? reading = null)
    {
        var step = 0;
        var source = new FakeSource();
        reading ??= i => Reading(1_000_000 + 400_000 * Math.Sin(i / 4.0) + i * 20_000, 200_000 + 50_000 * Math.Cos(i / 3.0));
        source.OnRead = () => Task.FromResult(reading(step++));
        var rig = Create(source);
        await rig.Module.StartAsync(CancellationToken.None);
        for (var i = 0; i < 60; i++) rig.Time.Advance(TimeSpan.FromSeconds(1));
        return rig;
    }

    private static SKBitmap RenderOn(NetworkModule module, SKRectI rect, GridSize size, float scale, Theme theme, int margin = 30)
    {
        var bitmap = new SKBitmap(GridGeometry.Width + 2 * margin, GridGeometry.Height + 2 * margin);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(theme.Background);
        var bounds = new SKRect(rect.Left + margin, rect.Top + margin, rect.Right + margin, rect.Bottom + margin);
        module.Render(canvas, new ModuleRenderContext(bounds, size, theme, scale, Now));
        return bitmap;
    }

    private static int Count(SKBitmap bitmap, SKRectI area, Func<SKColor, bool> predicate)
    {
        var count = 0;
        for (var y = Math.Max(0, area.Top); y < Math.Min(bitmap.Height, area.Bottom); y++)
            for (var x = Math.Max(0, area.Left); x < Math.Min(bitmap.Width, area.Right); x++)
                if (predicate(bitmap.GetPixel(x, y))) count++;
        return count;
    }

    private static SKRectI Inflate(SKRectI r, int margin) =>
        new(r.Left + margin, r.Top + margin, r.Right + margin, r.Bottom + margin);

    public static TheoryData<int, int, int, int, float> Presets()
    {
        var data = new TheoryData<int, int, int, int, float>();
        foreach (var scale in new[] { 0.85f, 1.0f, 1.2f })
        {
            data.Add(0, 3, 3, 1, scale);   // Compact (et taille minimale), contre le bord
            data.Add(13, 0, 3, 1, scale);
            data.Add(5, 2, 4, 2, scale);   // Idéal
            data.Add(10, 0, 6, 2, scale);  // Large
            data.Add(0, 0, 3, 2, scale);   // plus étroit que les préréglages
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public async Task Render_FitsInside_AllPresets(int col, int row, int cols, int rows, float scale)
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(col, row, cols, rows);
        var inside = Inflate(rect, 30);
        var whole = new SKRectI(0, 0, GridGeometry.Width + 60, GridGeometry.Height + 60);
        var states = new (string Name, Func<int, SourceReading> Reading)[]
        {
            ("en ligne", i => Reading(1_000_000 + 400_000 * Math.Sin(i / 4.0) + i * 20_000, 200_000 + 50_000 * Math.Cos(i / 3.0))),
            ("Gb/s", i => Reading(155_000_000 + i, 120_000_000 + i)),
            ("sans échantillon", _ => new SourceReading(null, new ModuleStatus(ModuleHealth.Online))),
            ("hors ligne", i => new SourceReading(Reading(1, 1).Sample, new ModuleStatus(ModuleHealth.Offline))),
            ("action", _ => new SourceReading(null, new ModuleStatus(ModuleHealth.NotConfigured,
                "Appuyez sur la flèche de la box pour autoriser le tableau de bord à lire son débit"))),
        };

        foreach (var (name, reading) in states)
        {
            using var rig = await CreateWithHistory(reading);
            using var bitmap = RenderOn(rig.Module, rect, new GridSize(cols, rows), scale, theme);

            var total = Count(bitmap, whole, c => c != theme.Background);
            var inner = Count(bitmap, inside, c => c != theme.Background);
            Assert.True(inner > 300, $"{name} : seulement {inner} pixels dessinés");
            Assert.True(total == inner, $"{name} : {total - inner} pixels hors du rectangle");
        }
    }

    [Fact]
    public async Task Render_Curve_UsesAccentAndMutedColors_InTwoRows()
    {
        var theme = Themes.Classique;
        using var rig = await CreateWithHistory();
        var rect = GridGeometry.ModuleRect(5, 2, 4, 2);

        using var bitmap = RenderOn(rig.Module, rect, new GridSize(4, 2), 1f, theme);
        var inside = Inflate(rect, 30);
        // Le bas du module : la courbe de 62 px, au-dessous des valeurs.
        var curveArea = new SKRectI(inside.Left, inside.Bottom - 62 - 18, inside.Right, inside.Bottom);

        Assert.True(Count(bitmap, curveArea, c => c == theme.Accent) > 100);
        Assert.True(Count(bitmap, curveArea, c => c == theme.TextMuted) > 50);
    }

    [Fact]
    public async Task Render_OneRow_ShowsValuesOnly()
    {
        var theme = Themes.Classique;
        using var rig = await CreateWithHistory();
        var rect = GridGeometry.ModuleRect(0, 3, 3, 1);
        using var oneRow = RenderOn(rig.Module, rect, new GridSize(3, 1), 1f, theme);
        var inside = Inflate(rect, 30);

        // Pas de titre (TextMuted en haut à gauche) ni de courbe : les flèches et les valeurs occupent la ligne.
        Assert.True(Count(oneRow, inside, c => c == theme.Accent) > 20); // flèche descendante pleine
        Assert.True(Count(oneRow, inside, c => c == theme.Text) > 100);

        var rect2 = GridGeometry.ModuleRect(5, 2, 4, 2);
        using var twoRows = RenderOn(rig.Module, rect2, new GridSize(4, 2), 1f, theme);
        var oneRowAccent = Count(oneRow, inside, c => c == theme.Accent);
        var twoRowsAccent = Count(twoRows, Inflate(rect2, 30), c => c == theme.Accent);
        Assert.True(twoRowsAccent > oneRowAccent * 2, $"{twoRowsAccent} contre {oneRowAccent}");
    }

    [Fact]
    public async Task Render_OfflineTitle_IsWarning_OnlineIsNot()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(10, 0, 6, 2);
        var inside = Inflate(rect, 30);
        var titleRow = new SKRectI(inside.Left, inside.Top, inside.Right, inside.Top + 50);

        using var online = await CreateWithHistory();
        using var onlineBitmap = RenderOn(online.Module, rect, new GridSize(6, 2), 1f, theme);
        Assert.Equal(0, Count(onlineBitmap, inside, c => c == theme.Warning));

        using var offline = await CreateWithHistory(_ => new SourceReading(Reading(1, 1).Sample, new ModuleStatus(ModuleHealth.Offline)));
        using var offlineBitmap = RenderOn(offline.Module, rect, new GridSize(6, 2), 1f, theme);
        Assert.True(Count(offlineBitmap, titleRow, c => c == theme.Warning) > 30);
    }

    [Fact]
    public async Task Module_ActionMessage_IsDrawn()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(5, 2, 4, 2);
        var inside = Inflate(rect, 30);
        var belowTitle = new SKRectI(inside.Left, inside.Top + 60, inside.Right, inside.Bottom);

        using var withMessage = await CreateWithHistory(_ => new SourceReading(null,
            new ModuleStatus(ModuleHealth.NotConfigured, "Appuyez sur la flèche de la box")));
        using var messageBitmap = RenderOn(withMessage.Module, rect, new GridSize(4, 2), 1f, theme);
        Assert.True(Count(messageBitmap, belowTitle, c => c == theme.Warning) > 100);

        // Hors ligne sans message : seul le titre est en Warning, rien sous lui.
        using var without = await CreateWithHistory(_ => new SourceReading(null, new ModuleStatus(ModuleHealth.Offline)));
        using var plainBitmap = RenderOn(without.Module, rect, new GridSize(4, 2), 1f, theme);
        Assert.Equal(0, Count(plainBitmap, belowTitle, c => c == theme.Warning));
    }

    [Fact]
    public async Task Module_ActionMessage_ReplacesValuesAndCurve()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(5, 2, 4, 2);
        var inside = Inflate(rect, 30);
        using var rig = await CreateWithHistory(_ => new SourceReading(Reading(1_000_000, 1).Sample,
            new ModuleStatus(ModuleHealth.NotConfigured, "Appuyez sur la flèche de la box")));

        using var bitmap = RenderOn(rig.Module, rect, new GridSize(4, 2), 1f, theme);

        Assert.Equal(0, Count(bitmap, inside, c => c == theme.Accent));
        Assert.Equal(0, Count(bitmap, inside, c => c == theme.Text));
    }

    [Fact]
    public void Render_BeforeStart_AndAfterDispose_DoesNotThrow()
    {
        var rig = Create();
        var rect = GridGeometry.ModuleRect(5, 2, 4, 2);

        using var bitmap = RenderOn(rig.Module, rect, new GridSize(4, 2), 1f, Themes.Classique);
        rig.Module.Dispose();
        using var afterDispose = RenderOn(rig.Module, rect, new GridSize(4, 2), 1f, Themes.Classique);
    }
}
