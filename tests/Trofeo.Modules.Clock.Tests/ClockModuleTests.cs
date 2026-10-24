using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.ModuleKit;
using Trofeo.Modules.Clock;

namespace Trofeo.Modules.Clock.Tests;

public class ClockModuleTests
{
    private static FakeTimeProvider At(int h, int m, int s) =>
        new(new DateTimeOffset(2026, 10, 24, h, m, s, TimeSpan.Zero));

    private static IModule Create(TimeProvider time, string json = "{}")
    {
        using var doc = JsonDocument.Parse(json);
        return new ClockFactory().Create(doc.RootElement.Clone(), time);
    }

    [Fact]
    public void Descriptor_MatchesBrief()
    {
        var d = new ClockFactory().Descriptor;
        Assert.Equal("clock", d.TypeId);
        Assert.Equal("Heure et date", d.DisplayName);
        Assert.Equal(new GridSize(3, 1), d.MinSize);
        Assert.Equal(new GridSize(3, 1), d.Presets[SizePreset.Compact]);
        Assert.Equal(new GridSize(4, 2), d.Presets[SizePreset.Ideal]);
        Assert.Equal(new GridSize(6, 2), d.Presets[SizePreset.Large]);
    }

    [Fact]
    public async Task StatusAndAnimation_AreConstant()
    {
        using var module = Create(At(14, 32, 0));
        await module.StartAsync(CancellationToken.None);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.False(module.IsAnimating);
    }

    [Fact]
    public async Task Invalidates_AtMinuteBoundary()
    {
        var time = At(14, 32, 50);
        using var module = Create(time);
        var count = 0;
        module.Invalidated += (_, _) => count++;
        await module.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(0, count);

        time.Advance(TimeSpan.FromSeconds(1)); // 14:33:00
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Invalidates_EveryMinute_WithoutDrift()
    {
        var time = At(14, 32, 50);
        using var module = Create(time);
        var count = 0;
        module.Invalidated += (_, _) => count++;
        await module.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(1, count);
        time.Advance(TimeSpan.FromSeconds(1)); // 14:34:00
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Dispose_StopsTimer()
    {
        var time = At(14, 32, 50);
        var module = Create(time);
        var count = 0;
        module.Invalidated += (_, _) => count++;
        await module.StartAsync(CancellationToken.None);
        module.Dispose();

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("{}", 1.0f)]
    [InlineData("""{"format":"12h"}""", 1.2f)]
    public void Render_DrawsNonBackgroundPixels(string json, float textScale)
    {
        var time = At(14, 32, 0);
        using var module = Create(time, json);
        var bg = new SKColor(0x10, 0x10, 0x10);
        var theme = new Theme("test", bg, bg, 1f, 0f, SKColors.White, SKColors.Gray,
            SKColors.Teal, SKColors.Orange, SKTypeface.Default);
        using var bitmap = new SKBitmap(600, 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(bg);

        module.Render(canvas, new ModuleRenderContext(
            new SKRect(0, 0, 600, 240), new GridSize(4, 2), theme, textScale, time.GetUtcNow()));

        var drawn = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y) != bg) drawn++;
        Assert.True(drawn > 500, $"seulement {drawn} pixels dessinés");
    }

    [Fact]
    public void Render_TimeWiderThanBounds_ScalesDownToFit()
    {
        var time = At(14, 32, 0);
        using var module = Create(time);
        var bg = new SKColor(0x10, 0x10, 0x10);
        var theme = new Theme("test", bg, bg, 1f, 0f, SKColors.White, SKColors.Gray,
            SKColors.Teal, SKColors.Orange, SKTypeface.Default);
        using var bitmap = new SKBitmap(200, 160);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(bg);

        module.Render(canvas, new ModuleRenderContext(
            new SKRect(0, 0, 200, 160), new GridSize(3, 1), theme, 1f, time.GetUtcNow()));

        // Le texte doit rester dans les limites : au moins un pixel dessiné, rien ne déborde (le bitmap borne déjà),
        // et la colonne la plus à droite (marge) reste vide.
        var rightColumnDrawn = false;
        var drawn = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y) != bg)
                {
                    drawn++;
                    if (x >= 190) rightColumnDrawn = true;
                }
        Assert.True(drawn > 100);
        Assert.False(rightColumnDrawn);
    }
}
