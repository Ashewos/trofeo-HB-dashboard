using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.ModuleKit;
using Trofeo.Sensors;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Monitoring.Tests;

public class GpuChoiceTests
{
    private const string Nvidia = "NVIDIA GeForce RTX 5070";
    private const string Intel = "Intel(R) UHD Graphics 770";

    private sealed class Feed(SensorSnapshot? latest) : ISensorFeed
    {
        public SensorSnapshot? Latest { get; } = latest;
        public event EventHandler? Updated { add { } remove { } }
        public IDisposable Acquire() => new Ticket();
        private sealed class Ticket : IDisposable { public void Dispose() { } }
    }

    private static readonly Theme TestTheme = new("test", SKColors.Black, SKColors.Black, 1f, 0f, SKColors.White, SKColors.Gray,
        SKColors.Teal, SKColors.Orange, SKTypeface.Default);

    private static SensorSnapshot TwoGpus() => new(
        "Intel(R) Core(TM) Ultra 7 265K", Nvidia,
        new Dictionary<string, double> { [SensorKeys.CpuLoad] = 34, [SensorKeys.CpuTemp] = 52, [SensorKeys.GpuLoad] = 61, [SensorKeys.GpuTemp] = 64 },
        Elevated: true,
        Gpus:
        [
            new GpuReading(Nvidia, new Dictionary<string, double> { [SensorKeys.GpuLoad] = 61, [SensorKeys.GpuTemp] = 64 }),
            new GpuReading(Intel, new Dictionary<string, double> { [SensorKeys.GpuLoad] = 7, [SensorKeys.GpuTemp] = 41 }),
        ]);

    private static (MonitoringModule Module, MonitoringFactory Factory) Create(SensorSnapshot? snapshot, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var factory = new MonitoringFactory(new Feed(snapshot));
        var context = new ModuleContext(
            Guid.NewGuid(), doc.RootElement.Clone(), new FakeTimeProvider(), new FakeModuleLog(), new FakeSecretStore());
        return ((MonitoringModule)factory.Create(context), factory);
    }

    // La ligne GPU telle que le module la dessinerait avec le dernier relevé.
    private static MonitoringRow GpuRow(MonitoringModule module) => MonitoringRows.Build("gpu", module.CurrentSnapshot);

    private static void Render(MonitoringModule module, float width = 400, float height = 300)
    {
        using var bitmap = new SKBitmap((int)width, (int)height);
        using var canvas = new SKCanvas(bitmap);
        module.Render(canvas, new ModuleRenderContext(
            new SKRect(0, 0, width, height), new GridSize(4, 2), TestTheme, 1f, new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("""{"gpu":"Intel(R) UHD Graphics 770"}""", "Intel(R) UHD Graphics 770")]
    [InlineData("""{"gpu":"  Intel(R) UHD Graphics 770  "}""", "Intel(R) UHD Graphics 770")]
    [InlineData("""{"gpu":""}""", null)]
    [InlineData("""{"gpu":"   "}""", null)]
    [InlineData("""{"gpu":3}""", null)]
    [InlineData("""{"gpu":null}""", null)]
    [InlineData("""{}""", null)]
    public void Settings_Gpu_IsTrimmedNonEmptyStringElseNull(string json, string? expected)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(expected, MonitoringSettings.FromJson(doc.RootElement).Gpu);
    }

    [Fact]
    public void Monitoring_ChosenGpu_IsDrawn()
    {
        var (module, _) = Create(TwoGpus(), $$"""{"gpu":"{{Intel}}","rows":["gpu"]}""");

        Assert.Equal("7 %", GpuRow(module).Value);
        Assert.Equal("41 °C", GpuRow(module).Secondary);
        Assert.Equal("Core Ultra 7 265K · UHD 770", module.CurrentTitle());
        Render(module);
        Assert.Empty(module.Notices);
    }

    [Fact]
    public void Monitoring_NoChoice_DrawsAutomaticCard()
    {
        var (module, _) = Create(TwoGpus(), """{"rows":["gpu"]}""");

        Assert.Equal("61 %", GpuRow(module).Value);
        Assert.Equal("Core Ultra 7 265K · RTX 5070", module.CurrentTitle());
    }

    [Fact]
    public void Monitoring_MissingGpu_FallsBackWithNotice()
    {
        var (module, _) = Create(TwoGpus(), """{"gpu":"Radeon X","rows":["gpu"]}""");

        Assert.Empty(module.Notices); // avant le premier rendu
        Render(module);

        Assert.Equal(["Carte « Radeon X » introuvable : choix automatique."], module.Notices);
        Assert.Equal("61 %", GpuRow(module).Value);
        Assert.Equal("Core Ultra 7 265K · RTX 5070", module.CurrentTitle());
    }

    [Fact]
    public void Monitoring_MissingGpu_NoticeComesBeforeRowCapacityNotice()
    {
        var (module, _) = Create(TwoGpus(), """{"gpu":"Radeon X","rows":["cpu","gpu","ram","vram","disk"]}""");

        Render(module, 192, 154);

        Assert.Equal(
            [
                "Carte « Radeon X » introuvable : choix automatique.",
                "2 lignes sur 5 tiennent à cette taille : agrandis le module ou retire des lignes.",
            ],
            module.Notices);
    }

    [Fact]
    public void Monitoring_GpuChosen_ButNoSnapshotYet_RaisesNoNotice()
    {
        var (module, _) = Create(null, """{"gpu":"Radeon X","rows":["gpu"]}""");

        Render(module);

        Assert.Empty(module.Notices);
    }

    [Fact]
    public void Monitoring_GpuChosen_OldHelperWithoutGpus_FallsBackWithNotice()
    {
        var old = new SensorSnapshot("CPU", Nvidia, new Dictionary<string, double> { [SensorKeys.GpuLoad] = 61 }, Elevated: true);
        var (module, _) = Create(old, $$"""{"gpu":"{{Intel}}","rows":["gpu"]}""");

        Render(module);

        Assert.Equal([$"Carte « {Intel} » introuvable : choix automatique."], module.Notices);
    }

    [Fact]
    public async Task Monitoring_LookupGpu_ListsDetectedCards()
    {
        var (_, factory) = Create(TwoGpus(), "{}");

        var results = await ((IConfigurableModuleFactory)factory).LookupAsync("gpu", "", CancellationToken.None);

        Assert.Equal([Nvidia, Intel], results.Select(r => r.Label));
        Assert.Equal([Nvidia, Intel], results.Select(r => r.Value.GetString()));
    }

    [Fact]
    public async Task Monitoring_LookupGpu_WithoutSnapshotOrGpus_IsEmpty()
    {
        var none = new MonitoringFactory(new Feed(null));
        var old = new MonitoringFactory(new Feed(new SensorSnapshot("CPU", null, new Dictionary<string, double>(), Elevated: false)));

        Assert.Empty(await none.LookupAsync("gpu", "", CancellationToken.None));
        Assert.Empty(await old.LookupAsync("gpu", "", CancellationToken.None));
        Assert.Empty(await new MonitoringFactory(new Feed(TwoGpus())).LookupAsync("title", "", CancellationToken.None));
    }

    [Fact]
    public void Schema_HasGpuLookup()
    {
        var factory = new MonitoringFactory(new Feed(null));

        var field = Assert.IsType<LookupField>(Assert.Single(factory.Schema.Fields, f => f.Key == "gpu"));
        Assert.Equal("Carte graphique surveillée", field.Label);
        Assert.False(field.Searchable);
        Assert.Equal("Automatique (celle qui a le plus de mémoire)", field.EmptyLabel);
    }
}
