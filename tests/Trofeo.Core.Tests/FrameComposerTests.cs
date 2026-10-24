using System.Text.Json;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Core.Tests;

public class FrameComposerTests
{
    private static ModulePlacement Place(Guid id, int column, int row, int columns, int rows) =>
        new(id, "fake", column, row, columns, rows, TextSize.Normal, JsonDocument.Parse("{}").RootElement.Clone());

    private static void AssertClose(SKColor expected, SKColor actual, int tolerance = 2)
    {
        Assert.InRange(actual.Red, expected.Red - tolerance, expected.Red + tolerance);
        Assert.InRange(actual.Green, expected.Green - tolerance, expected.Green + tolerance);
        Assert.InRange(actual.Blue, expected.Blue - tolerance, expected.Blue + tolerance);
    }

    [Fact]
    public void Compose_Size()
    {
        using var frame = new FrameComposer().Compose(
            new DashboardLayout(0, []), new Dictionary<Guid, IModule>(), Themes.Classique, DateTimeOffset.UnixEpoch);

        Assert.Equal(1920, frame.Width);
        Assert.Equal(462, frame.Height);
    }

    [Fact]
    public void Compose_PaintsPanelInModuleRect()
    {
        var id = Guid.NewGuid();
        var layout = new DashboardLayout(0, [Place(id, 12, 0, 4, 2)]);
        var modules = new Dictionary<Guid, IModule> { [id] = new FakeModule() };
        var theme = Themes.Classique;

        using var frame = new FrameComposer().Compose(layout, modules, theme, DateTimeOffset.UnixEpoch);

        Assert.Equal(theme.Background, frame.GetPixel(5, 5));

        var inside = frame.GetPixel(1500, 100);
        Assert.NotEqual(theme.Background, inside);
        Assert.Equal(255, inside.Alpha);
        float o = theme.PanelOpacity;
        var expected = new SKColor(
            (byte)Math.Round(theme.Panel.Red * o + theme.Background.Red * (1 - o)),
            (byte)Math.Round(theme.Panel.Green * o + theme.Background.Green * (1 - o)),
            (byte)Math.Round(theme.Panel.Blue * o + theme.Background.Blue * (1 - o)));
        AssertClose(expected, inside);
    }

    [Fact]
    public void Compose_PassesContextToRender()
    {
        var id = Guid.NewGuid();
        var module = new FakeModule();
        var layout = new DashboardLayout(0, [Place(id, 12, 0, 4, 2) with { TextSize = TextSize.Large }]);
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        using var frame = new FrameComposer().Compose(
            layout, new Dictionary<Guid, IModule> { [id] = module }, Themes.Classique, now);

        var call = Assert.Single(module.RenderCalls);
        var rect = GridGeometry.ModuleRect(12, 0, 4, 2);
        Assert.Equal(SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height), call.Bounds);
        Assert.Equal(new GridSize(4, 2), call.Size);
        Assert.Equal(1.2f, call.TextScale);
        Assert.Equal(now, call.Now);
        Assert.Same(Themes.Classique, call.Theme);
    }

    [Fact]
    public void Compose_PlacementWithoutInstance_PaintsPanelOnly()
    {
        var layout = new DashboardLayout(0, [Place(Guid.NewGuid(), 12, 0, 4, 2)]);

        using var frame = new FrameComposer().Compose(
            layout, new Dictionary<Guid, IModule>(), Themes.Classique, DateTimeOffset.UnixEpoch);

        Assert.NotEqual(Themes.Classique.Background, frame.GetPixel(1500, 100));
    }

    [Fact]
    public void Compose_ModuleThrows_DrawsErrorPanelOnly()
    {
        var badId = Guid.NewGuid();
        var goodId = Guid.NewGuid();
        var green = new SKColor(0, 255, 0);
        var layout = new DashboardLayout(0, [Place(badId, 0, 0, 4, 2), Place(goodId, 12, 0, 4, 2)]);
        var modules = new Dictionary<Guid, IModule>
        {
            [badId] = new FakeModule(throwOnRender: true),
            [goodId] = new FakeModule((canvas, ctx) => canvas.DrawRect(ctx.Bounds, new SKPaint { Color = green })),
        };

        using var frame = new FrameComposer().Compose(layout, modules, Themes.Classique, DateTimeOffset.UnixEpoch);

        Assert.Equal(green, frame.GetPixel(1500, 100));

        // Le panneau d'erreur contient du texte Warning : au moins un pixel de la zone en est proche.
        var bad = GridGeometry.ModuleRect(0, 0, 4, 2);
        var warning = Themes.Classique.Warning;
        bool foundWarning = false;
        for (int y = bad.Top; y < bad.Bottom && !foundWarning; y++)
        {
            for (int x = bad.Left; x < bad.Right; x++)
            {
                var p = frame.GetPixel(x, y);
                if (Math.Abs(p.Red - warning.Red) < 40 && Math.Abs(p.Green - warning.Green) < 40 && Math.Abs(p.Blue - warning.Blue) < 40)
                {
                    foundWarning = true;
                    break;
                }
            }
        }

        Assert.True(foundWarning);
    }

    [Fact]
    public void Compose_ModuleDrawsOutsideBounds_IsClipped()
    {
        var id = Guid.NewGuid();
        var layout = new DashboardLayout(0, [Place(id, 12, 0, 4, 2)]);
        var modules = new Dictionary<Guid, IModule>
        {
            [id] = new FakeModule((canvas, _) => canvas.DrawRect(0, 0, 1920, 462, new SKPaint { Color = SKColors.Red })),
        };

        using var frame = new FrameComposer().Compose(layout, modules, Themes.Classique, DateTimeOffset.UnixEpoch);

        Assert.Equal(Themes.Classique.Background, frame.GetPixel(5, 5));
        Assert.Equal(SKColors.Red, frame.GetPixel(1500, 100));
    }

    [Fact]
    public void Compose_ModuleFillsBounds_IsClippedToRoundedCorners()
    {
        var id = Guid.NewGuid();
        var layout = new DashboardLayout(0, [Place(id, 12, 0, 4, 2)]);
        var modules = new Dictionary<Guid, IModule>
        {
            [id] = new FakeModule((canvas, ctx) => canvas.DrawRect(ctx.Bounds, new SKPaint { Color = SKColors.Red })),
        };
        var rect = GridGeometry.ModuleRect(12, 0, 4, 2);

        using var frame = new FrameComposer().Compose(layout, modules, Themes.Classique, DateTimeOffset.UnixEpoch);

        Assert.Equal(Themes.Classique.Background, frame.GetPixel(rect.Left, rect.Top)); // le coin arrondi n'est pas rouge
        Assert.Equal(SKColors.Red, frame.GetPixel(rect.Left + 40, rect.Top + 40));
    }

    [Fact]
    public void Compose_ModuleKeepsThrowing_LogsOnlyOnce()
    {
        var id = Guid.NewGuid();
        var layout = new DashboardLayout(0, [Place(id, 12, 0, 4, 2)]);
        var modules = new Dictionary<Guid, IModule> { [id] = new FakeModule(throwOnRender: true) };
        var lines = new List<string>();
        var composer = new FrameComposer(new RateLimitedErrorLog(TimeProvider.System, lines.Add));

        for (var i = 0; i < 5; i++)
            composer.Compose(layout, modules, Themes.Classique, DateTimeOffset.UnixEpoch).Dispose();

        Assert.Single(lines);
        Assert.Contains("fake", lines[0]);
    }
}
