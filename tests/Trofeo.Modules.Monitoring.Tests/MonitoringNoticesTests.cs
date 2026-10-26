using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.ModuleKit;
using Trofeo.Sensors;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Monitoring.Tests;

public class MonitoringNoticesTests
{
    private sealed class QuietFeed : ISensorFeed
    {
        public SensorSnapshot? Latest => null;
        public event EventHandler? Updated { add { } remove { } }
        public IDisposable Acquire() => new Ticket();
        private sealed class Ticket : IDisposable { public void Dispose() { } }
    }

    private static readonly Theme TestTheme = new("test", SKColors.Black, SKColors.Black, 1f, 0f, SKColors.White, SKColors.Gray,
        SKColors.Teal, SKColors.Orange, SKTypeface.Default);

    private static MonitoringModule Create(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var context = new ModuleContext(
            Guid.NewGuid(), doc.RootElement.Clone(), new FakeTimeProvider(), new FakeModuleLog(), new FakeSecretStore());
        return (MonitoringModule)new MonitoringFactory(new QuietFeed()).Create(context);
    }

    private static void Render(MonitoringModule module, float width, float height)
    {
        using var bitmap = new SKBitmap((int)width, (int)height);
        using var canvas = new SKCanvas(bitmap);
        module.Render(canvas, new ModuleRenderContext(
            new SKRect(0, 0, width, height), new GridSize(4, 2), TestTheme, 1f, new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Monitoring_TooManyRows_RaisesNotice()
    {
        // 154 px de haut : (154 − 62 + 10) / 50 → 2 lignes.
        var module = Create("""{"rows":["cpu","gpu","ram","vram","disk"]}""");
        Assert.Empty(module.Notices); // avant le premier rendu : rien à dire

        Render(module, 192, 154);

        Assert.Equal(["2 lignes sur 5 tiennent à cette taille : agrandis le module ou retire des lignes."], module.Notices);
    }

    [Fact]
    public void Monitoring_FittingRows_RaiseNoNotice()
    {
        var module = Create("""{"rows":["cpu","gpu"]}""");

        Render(module, 192, 154);

        Assert.Empty(module.Notices);
    }

    [Fact]
    public void Monitoring_ExactlyOneFittingRow_UsesSingularForm()
    {
        // 62 px de marges et de titre, puis 40 px par ligne : 110 px de haut → 1 ligne.
        var module = Create("""{"rows":["cpu","gpu","ram"]}""");

        Render(module, 192, 110);

        Assert.Equal(["1 ligne sur 3 tient à cette taille : agrandis le module ou retire des lignes."], module.Notices);
    }

    [Fact]
    public void Monitoring_NoFittingRow_UsesPluralWithZero()
    {
        var module = Create("""{"rows":["cpu","gpu"]}""");

        Render(module, 192, 40);

        Assert.Equal(["0 lignes sur 2 tiennent à cette taille : agrandis le module ou retire des lignes."], module.Notices);
    }

    [Fact]
    public void Monitoring_NoticeClears_WhenModuleGrows()
    {
        var module = Create("""{"rows":["cpu","gpu","ram"]}""");
        Render(module, 192, 110);
        Assert.Single(module.Notices);

        Render(module, 192, 300);

        Assert.Empty(module.Notices);
    }
}
