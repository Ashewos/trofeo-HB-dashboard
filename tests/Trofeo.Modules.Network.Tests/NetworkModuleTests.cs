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

        /// <summary>Si renseigné, remplace <see cref="OnRead"/> et reçoit le jeton de la lecture.</summary>
        public Func<CancellationToken, Task<SourceReading>>? OnReadWithToken { get; set; }

        public string Title => "Fake";
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
        public int Reads { get; private set; }
        public int Disposed { get; private set; }

        public Task<SourceReading> ReadAsync(CancellationToken ct)
        {
            Reads++;
            return OnReadWithToken is { } withToken ? withToken(ct) : OnRead();
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

    [Theory]
    [InlineData("pc", ModuleHealth.Online, null)]
    [InlineData("freebox", ModuleHealth.NotConfigured, "Appuie sur ► sur ta Freebox pour autoriser le dashboard")]
    [InlineData("unifi", ModuleHealth.NotConfigured, "Indique l'adresse de la console UniFi dans le logiciel de réglage")]
    public async Task CreateProduction_BuildsAWorkingModuleForEverySource(string source, ModuleHealth health, string? message)
    {
        // Aucun appel réseau : la « box » est un gestionnaire factice.
        using var http = new HttpClient(new FakeBoxHandler());
        using var doc = JsonDocument.Parse($$"""{"source":"{{source}}"}""");
        var context = new ModuleContext(Guid.NewGuid(), doc.RootElement.Clone(), TimeProvider.System, new FakeModuleLog(), new FakeSecretStore());

        using var module = NetworkFactory.CreateProduction(http).Create(context);
        await module.StartAsync(CancellationToken.None);

        Assert.Equal(health, module.Status.Health);
        Assert.Equal(message, module.Status.ActionMessage);
    }

    [Fact]
    public async Task CreateProduction_UniFiWithHost_WaitsForTheKey()
    {
        using var http = new HttpClient(new FakeBoxHandler());
        using var doc = JsonDocument.Parse("""{"source":"unifi","unifi":{"host":"192.168.1.1"}}""");
        var context = new ModuleContext(Guid.NewGuid(), doc.RootElement.Clone(), TimeProvider.System, new FakeModuleLog(), new FakeSecretStore());

        using var module = NetworkFactory.CreateProduction(http).Create(context);
        await module.StartAsync(CancellationToken.None); // pas de clé : aucun appel réseau

        Assert.Equal(ModuleHealth.NotConfigured, module.Status.Health);
        Assert.Equal("Ajoute la clé API UniFi dans le logiciel de réglage", module.Status.ActionMessage);
    }

    private sealed class FakeBoxHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api_version" => """{"api_version":"8.2","api_base_url":"/api/"}""",
                _ => """{"success":true,"result":{"app_token":"t","track_id":1}}""",
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
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

        // Le minuteur est réarmé après chaque lecture : une lecture par intervalle écoulé, jamais de rattrapage.
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(4, rig.Source.Reads);

        rig.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(5, rig.Source.Reads);
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

        // Le minuteur est réarmé une fois la lecture terminée : on avance jusqu'à la prochaine lecture.
        var spin = new SpinWait();
        for (var i = 0; i < 50 && rig.Source.Reads < 3; i++)
        {
            rig.Time.Advance(TimeSpan.FromSeconds(1));
            spin.SpinOnce();
        }
        Assert.Equal(3, rig.Source.Reads);
    }

    [Fact]
    public async Task Interval_IsReadAgainAfterEveryRead()
    {
        var rig = Create(new FakeSource { Interval = TimeSpan.FromSeconds(2) });
        await rig.Module.StartAsync(CancellationToken.None);
        rig.Source.Interval = TimeSpan.FromSeconds(1); // la source passe de 2 s à 1 s (autorisation accordée)

        rig.Time.Advance(TimeSpan.FromSeconds(2)); // lecture qui se termine : le minuteur est réarmé à 1 s
        Assert.Equal(2, rig.Source.Reads);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, rig.Source.Reads);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(4, rig.Source.Reads);

        rig.Source.Interval = TimeSpan.FromSeconds(5); // et inversement
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(5, rig.Source.Reads);
        rig.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(5, rig.Source.Reads);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(6, rig.Source.Reads);
    }

    [Theory]
    [InlineData(1, 10)]  // le plancher de 10 s
    [InlineData(5, 15)]  // 3 × Interval
    public async Task Read_NeverCompleting_TimesOutAsOfflineWithWarning(int intervalSeconds, int limitSeconds)
    {
        var rig = Create(new FakeSource { Interval = TimeSpan.FromSeconds(intervalSeconds) });
        await rig.Module.StartAsync(CancellationToken.None);
        Assert.Equal(ModuleHealth.Online, rig.Module.Status.Health);
        var stuck = new TaskCompletionSource<SourceReading>(); // ignore le jeton : ne rend la main que quand le test le décide
        rig.Source.OnRead = () => stuck.Task;

        rig.Time.Advance(TimeSpan.FromSeconds(intervalSeconds)); // la lecture démarre
        Assert.Equal(2, rig.Source.Reads);
        rig.Time.Advance(TimeSpan.FromSeconds(limitSeconds - 1));
        Assert.Equal(ModuleHealth.Online, rig.Module.Status.Health);

        rig.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(SpinWait.SpinUntil(() => rig.Module.Status.Health == ModuleHealth.Offline, TimeSpan.FromSeconds(5)));
        Assert.Contains(rig.Log.Entries, e => e.StartsWith("Warning"));
        Assert.DoesNotContain(rig.Log.Entries, e => e.StartsWith("Error"));
        Assert.Single(rig.Module.HistorySnapshot()); // l'historique est conservé

        // Le module continue ensuite à lire, une fois la lecture abandonnée réellement terminée.
        stuck.SetResult(Reading(99, 99));
        rig.Source.OnRead = () => Task.FromResult(Reading(1, 1));
        var spin = new SpinWait();
        for (var i = 0; i < 50 && rig.Module.Status.Health != ModuleHealth.Online; i++)
        {
            rig.Time.Advance(TimeSpan.FromSeconds(intervalSeconds));
            spin.SpinOnce();
        }
        Assert.Equal(ModuleHealth.Online, rig.Module.Status.Health);
    }

    [Fact]
    public async Task AbandonedRead_BlocksNewReadsUntilItReallyEnds()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        var stuck = new TaskCompletionSource<SourceReading>(); // source qui ignore le jeton
        rig.Source.OnRead = () => stuck.Task;
        rig.Time.Advance(TimeSpan.FromSeconds(1)); // lecture 2 : ne se termine pas
        Assert.Equal(2, rig.Source.Reads);
        rig.Time.Advance(TimeSpan.FromSeconds(10)); // délai écoulé
        Assert.True(SpinWait.SpinUntil(() => rig.Module.Status.Health == ModuleHealth.Offline, TimeSpan.FromSeconds(5)));
        Assert.Contains(rig.Log.Entries, e => e.StartsWith("Warning"));
        rig.Source.OnRead = () => Task.FromResult(Reading(1, 1));

        // La lecture abandonnée court encore : aucune autre n'est lancée, quel que soit le temps écoulé.
        for (var i = 0; i < 30; i++) rig.Time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        for (var i = 0; i < 30; i++) rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, rig.Source.Reads);
        Assert.Equal(ModuleHealth.Offline, rig.Module.Status.Health);

        // Elle se termine (son résultat tardif est ignoré) : le module relit à l'intervalle suivant.
        stuck.SetResult(Reading(99, 99));
        var spin = new SpinWait();
        for (var i = 0; i < 50 && rig.Source.Reads < 3; i++)
        {
            rig.Time.Advance(TimeSpan.FromSeconds(1));
            spin.SpinOnce();
        }
        Assert.Equal(3, rig.Source.Reads);
        Assert.True(SpinWait.SpinUntil(() => rig.Module.Status.Health == ModuleHealth.Online, TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain(rig.Module.HistorySnapshot(), h => h.Sample.DownBytesPerSecond == 99);
    }

    [Fact]
    public async Task Read_TimesOut_CancelsTheTokenGivenToTheSource()
    {
        CancellationToken seen = default;
        var source = new FakeSource
        {
            OnReadWithToken = ct =>
            {
                seen = ct;
                return Task.Delay(Timeout.Infinite, ct).ContinueWith<SourceReading>(_ => throw new InvalidOperationException("annulée"));
            },
        };
        var rig = Create(source);
        var start = rig.Module.StartAsync(CancellationToken.None); // la première lecture ne se termine pas

        rig.Time.Advance(TimeSpan.FromSeconds(10));
        await start;

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(ModuleHealth.Offline, rig.Module.Status.Health);
        Assert.Contains(rig.Log.Entries, e => e.StartsWith("Warning"));
        Assert.DoesNotContain(rig.Log.Entries, e => e.StartsWith("Error"));
    }

    [Fact]
    public async Task Dispose_WhileReading_LogsNoError()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        var gate = new TaskCompletionSource<SourceReading>();
        rig.Source.OnRead = () => gate.Task;
        rig.Time.Advance(TimeSpan.FromSeconds(1)); // lecture en cours

        rig.Module.Dispose();
        gate.SetException(new ObjectDisposedException("source")); // la source libérée échoue à sa lecture en cours
        await Task.Delay(100);

        Assert.DoesNotContain(rig.Log.Entries, e => e.StartsWith("Error"));
        Assert.DoesNotContain(rig.Log.Entries, e => e.StartsWith("Warning"));
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

        for (var i = 0; i < 3; i++) rig.Time.Advance(TimeSpan.FromSeconds(1));
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

    // --- Valeurs périmées et état affiché ---

    [Fact]
    public async Task FailedRead_ClearsTheValues_ButKeepsTheCurve()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(5, 2, 4, 2);
        var inside = Inflate(rect, 30);
        var valuesArea = new SKRectI(inside.Left, inside.Top + 50, inside.Right, inside.Bottom - 100);
        var curveArea = new SKRectI(inside.Left, inside.Bottom - 62 - 18, inside.Right, inside.Bottom);
        using var live = await CreateWithHistory();
        using var failing = await CreateWithHistory(i => i < 30
            ? Reading(1_000_000 + 20_000 * i, 200_000)
            : throw new InvalidOperationException("coupé"));
        using var empty = await CreateWithHistory(_ => new SourceReading(null, new ModuleStatus(ModuleHealth.Online)));

        Assert.Null(failing.Module.LatestSample);
        Assert.NotNull(live.Module.LatestSample);
        Assert.Equal(ModuleHealth.Offline, failing.Module.Status.Health);
        Assert.Equal(30, failing.Module.HistorySnapshot().Count); // la courbe garde ce qui a été lu

        using var failingBitmap = RenderOn(failing.Module, rect, new GridSize(4, 2), 1f, theme);
        using var emptyBitmap = RenderOn(empty.Module, rect, new GridSize(4, 2), 1f, theme);
        // Les valeurs sont « — » sans unité : exactement ce que montre une source sans échantillon.
        for (var y = valuesArea.Top; y < valuesArea.Bottom; y++)
            for (var x = valuesArea.Left; x < valuesArea.Right; x++)
                Assert.Equal(emptyBitmap.GetPixel(x, y), failingBitmap.GetPixel(x, y));
        Assert.True(Count(failingBitmap, curveArea, c => c == theme.Accent) > 20);
    }

    [Fact]
    public async Task TimedOutRead_ClearsTheValues()
    {
        var rig = Create();
        await rig.Module.StartAsync(CancellationToken.None);
        Assert.NotNull(rig.Module.LatestSample);
        rig.Source.OnRead = () => new TaskCompletionSource<SourceReading>().Task;

        rig.Time.Advance(TimeSpan.FromSeconds(1)); // lecture lancée, jamais terminée
        rig.Time.Advance(TimeSpan.FromSeconds(10)); // délai écoulé

        Assert.True(SpinWait.SpinUntil(() => rig.Module.Status.Health == ModuleHealth.Offline, TimeSpan.FromSeconds(5)));
        Assert.Null(rig.Module.LatestSample);
        Assert.Single(rig.Module.HistorySnapshot());
    }

    [Fact]
    public async Task Render_OneRow_NotOnline_ShowsOfflineInWarning_InsteadOfValues()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(0, 3, 3, 1);
        var inside = Inflate(rect, 30);

        using var online = await CreateWithHistory();
        using var onlineBitmap = RenderOn(online.Module, rect, new GridSize(3, 1), 1f, theme);
        Assert.Equal(0, Count(onlineBitmap, inside, c => c == theme.Warning));

        using var offline = await CreateWithHistory(_ => new SourceReading(Reading(1_000_000, 1).Sample, new ModuleStatus(ModuleHealth.Offline)));
        using var offlineBitmap = RenderOn(offline.Module, rect, new GridSize(3, 1), 1f, theme);
        Assert.True(Count(offlineBitmap, inside, c => c == theme.Warning) > 100);
        Assert.Equal(0, Count(offlineBitmap, inside, c => c == theme.Text)); // pas de valeurs
        Assert.Equal(0, Count(offlineBitmap, inside, c => c == theme.Accent));

        using var failed = await CreateWithHistory(_ => throw new InvalidOperationException("coupé"));
        using var failedBitmap = RenderOn(failed.Module, rect, new GridSize(3, 1), 1f, theme);
        Assert.True(Count(failedBitmap, inside, c => c == theme.Warning) > 100);
        Assert.Equal(0, Count(failedBitmap, inside, c => c == theme.Text));
    }

    [Fact]
    public async Task Render_OneRow_NotOnline_WithActionMessage_DrawsTheMessage()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(0, 3, 3, 1);
        var inside = Inflate(rect, 30);
        using var offline = await CreateWithHistory(_ => new SourceReading(null, new ModuleStatus(ModuleHealth.Offline)));
        using var withMessage = await CreateWithHistory(_ => new SourceReading(null,
            new ModuleStatus(ModuleHealth.Offline, "Statistiques UniFi indisponibles pour cette passerelle")));

        using var offlineBitmap = RenderOn(offline.Module, rect, new GridSize(3, 1), 1f, theme);
        using var messageBitmap = RenderOn(withMessage.Module, rect, new GridSize(3, 1), 1f, theme);

        Assert.True(Count(messageBitmap, inside, c => c == theme.Warning) > 100);
        Assert.Equal(0, Count(messageBitmap, inside, c => c == theme.Text));
        Assert.NotEqual(Count(offlineBitmap, inside, c => c == theme.Warning), Count(messageBitmap, inside, c => c == theme.Warning));
    }

    [Fact]
    public async Task Render_Title_NotConfigured_SaysToConfigure_InWarning()
    {
        var theme = Themes.Classique;
        var rect = GridGeometry.ModuleRect(10, 0, 6, 2);
        var inside = Inflate(rect, 30);
        var titleRow = new SKRectI(inside.Left, inside.Top, inside.Right, inside.Top + 50);
        using var offline = await CreateWithHistory(_ => new SourceReading(null, new ModuleStatus(ModuleHealth.Offline)));
        using var notConfigured = await CreateWithHistory(_ => new SourceReading(null, new ModuleStatus(ModuleHealth.NotConfigured)));

        using var offlineBitmap = RenderOn(offline.Module, rect, new GridSize(6, 2), 1f, theme);
        using var notConfiguredBitmap = RenderOn(notConfigured.Module, rect, new GridSize(6, 2), 1f, theme);

        Assert.True(Count(notConfiguredBitmap, titleRow, c => c == theme.Warning) > 30);
        // « à configurer » et « hors ligne » n'ont pas la même largeur : le mot commence à un autre endroit.
        Assert.NotEqual(LeftmostWarning(offlineBitmap, titleRow, theme), LeftmostWarning(notConfiguredBitmap, titleRow, theme));
    }

    private static int LeftmostWarning(SKBitmap bitmap, SKRectI area, Theme theme)
    {
        for (var x = area.Left; x < area.Right; x++)
            for (var y = area.Top; y < area.Bottom; y++)
                if (bitmap.GetPixel(x, y) == theme.Warning) return x;
        return -1;
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
