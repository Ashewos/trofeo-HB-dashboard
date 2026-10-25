using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.ModuleKit;
using Trofeo.Modules.Clock;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Clock.Tests;

public class ClockModuleTests
{
    private static FakeTimeProvider At(int h, int m, int s) =>
        new(new DateTimeOffset(2026, 10, 24, h, m, s, TimeSpan.Zero));

    private const string LyonJson = """{"location":{"name":"Lyon","latitude":45.76,"longitude":4.84}}""";

    private static readonly SKColor Bg = new(0x10, 0x10, 0x10);

    private static readonly Theme TestTheme = new("test", Bg, Bg, 1f, 0f, SKColors.White, SKColors.Gray,
        SKColors.Teal, SKColors.Orange, SKTypeface.Default);

    private static IModule Create(TimeProvider time, string json = "{}", IWeatherProvider? weather = null, FakeModuleLog? log = null)
    {
        using var doc = JsonDocument.Parse(json);
        return new ClockFactory(weather ?? new FakeWeatherProvider()).Create(new ModuleContext(
            Guid.NewGuid(), doc.RootElement.Clone(), time, log ?? new FakeModuleLog(), new FakeSecretStore()));
    }

    private static ClockModule CreateClock(TimeProvider time, FakeWeatherProvider weather, string json = LyonJson, FakeModuleLog? log = null) =>
        (ClockModule)Create(time, json, weather, log);

    private static WeatherNow Sunny => new(12.4, 2, true);

    private static int CountPixels(SKBitmap bitmap, SKColor color, int fromX = 0)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = fromX; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y) == color) count++;
        return count;
    }

    private static SKBitmap RenderTo(IModule module, DateTimeOffset now, int width, int height, GridSize size, float textScale = 1f)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Bg);
        module.Render(canvas, new ModuleRenderContext(new SKRect(0, 0, width, height), size, TestTheme, textScale, now));
        return bitmap;
    }

    [Fact]
    public void Descriptor_MatchesBrief()
    {
        var d = new ClockFactory(new FakeWeatherProvider()).Descriptor;
        Assert.Equal("clock", d.TypeId);
        Assert.Equal("Heure, date et météo", d.DisplayName);
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

    [Fact]
    public void Clock_TimeUsesHighlight()
    {
        var time = At(14, 32, 0);
        using var module = Create(time);
        var highlight = new SKColor(0xFF, 0x00, 0xFF);
        var theme = TestTheme with { Highlight = highlight };
        using var bitmap = new SKBitmap(600, 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Bg);

        module.Render(canvas, new ModuleRenderContext(new SKRect(0, 0, 600, 240), new GridSize(4, 2), theme, 1f, time.GetUtcNow()));

        // L'heure est en Highlight (rien en Text) ; la date reste en TextMuted.
        Assert.True(CountPixels(bitmap, highlight) > 200);
        Assert.Equal(0, CountPixels(bitmap, theme.Text));
        Assert.True(CountPixels(bitmap, theme.TextMuted) > 50);
    }

    [Fact]
    public async Task Clock_WeatherTemperatureUsesText_NotHighlight()
    {
        var time = At(14, 32, 0);
        var highlight = new SKColor(0xFF, 0x00, 0xFF);
        var text = new SKColor(0x12, 0xAB, 0x34); // aucune icône météo n'a cette couleur
        var theme = TestTheme with { Highlight = highlight, Text = text };
        var context = new ModuleRenderContext(new SKRect(0, 0, 600, 240), new GridSize(4, 2), theme, 1f, time.GetUtcNow());

        using var without = CreateClock(time, new FakeWeatherProvider(), "{}");
        using var before = new SKBitmap(600, 240);
        using (var canvas = new SKCanvas(before)) { canvas.Clear(Bg); without.Render(canvas, context); }
        var timePixels = CountPixels(before, highlight);
        Assert.True(timePixels > 200);
        Assert.Equal(0, CountPixels(before, text));

        using var module = CreateClock(time, new FakeWeatherProvider());
        await module.StartAsync(CancellationToken.None);
        using var after = new SKBitmap(600, 240);
        using (var canvas = new SKCanvas(after)) { canvas.Clear(Bg); module.Render(canvas, context); }

        Assert.True(CountPixels(after, text) > 30, "la température doit être dessinée en Text");
        Assert.Equal(timePixels, CountPixels(after, highlight)); // seule l'heure est en Highlight
    }

    // --- Météo ---

    [Fact]
    public async Task Weather_NoLocation_NeverFetches_AndStaysOnline()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider();
        using var module = CreateClock(time, weather, "{}");

        await module.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, weather.Calls);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.Null(module.CurrentWeather());
    }

    [Fact]
    public async Task Weather_FetchesAtStartThenEvery15Min()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider();
        using var module = CreateClock(time, weather, """{"unit":"f","location":{"name":"Lyon","latitude":45.76,"longitude":4.84}}""");

        await module.StartAsync(CancellationToken.None);
        Assert.Equal(1, weather.Calls);
        Assert.Equal(new WeatherLocation("Lyon", 45.76, 4.84), weather.LastLocation);
        Assert.Equal(TemperatureUnit.Fahrenheit, weather.LastUnit);
        Assert.Equal(Sunny, module.CurrentWeather());

        time.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.Equal(1, weather.Calls);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, weather.Calls);

        time.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(3, weather.Calls);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
    }

    [Fact]
    public async Task Weather_Success_RaisesInvalidated_NotStatusChanged()
    {
        var time = At(14, 32, 0);
        using var module = CreateClock(time, new FakeWeatherProvider());
        var invalidated = 0;
        var statusChanged = 0;
        module.Invalidated += (_, _) => invalidated++;
        module.StatusChanged += (_, _) => statusChanged++;

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(1, invalidated);
        Assert.Equal(0, statusChanged);
    }

    [Fact]
    public async Task Weather_FailureRetriesAfter2Min_AndGoesOffline()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider { Respond = () => throw new WeatherException("réseau coupé") };
        var log = new FakeModuleLog();
        using var module = CreateClock(time, weather, log: log);
        var statusChanged = 0;
        var invalidated = 0;
        module.StatusChanged += (_, _) => statusChanged++;
        module.Invalidated += (_, _) => invalidated++;

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(1, weather.Calls);
        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        Assert.Equal(1, statusChanged);
        Assert.Equal(1, invalidated);
        Assert.Equal(["Warning : Météo indisponible : réseau coupé"], log.Entries);

        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(59));
        Assert.Equal(1, weather.Calls);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, weather.Calls);
        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        Assert.Equal(1, statusChanged); // pas de nouveau changement tant que l'état reste le même

        weather.Respond = () => Task.FromResult(Sunny);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(3, weather.Calls);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.Equal(2, statusChanged);
        Assert.Equal(Sunny, module.CurrentWeather());

        time.Advance(TimeSpan.FromMinutes(15)); // retour au rythme normal
        Assert.Equal(4, weather.Calls);
    }

    [Fact]
    public async Task Weather_UnexpectedException_GoesOffline_AndIsLoggedAsError()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider { Respond = () => throw new InvalidOperationException("boum") };
        var log = new FakeModuleLog();
        using var module = CreateClock(time, weather, log: log);

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        var entry = Assert.Single(log.Entries);
        Assert.StartsWith("Error : ", entry);
        Assert.Contains("boum", entry);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(2, weather.Calls);
    }

    [Fact]
    public async Task Weather_FailureKeepsLastValue_UntilItIsStale()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider();
        using var module = CreateClock(time, weather);
        await module.StartAsync(CancellationToken.None);
        weather.Respond = () => throw new WeatherException("hors ligne");

        time.Advance(TimeSpan.FromMinutes(15));

        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        Assert.Equal(Sunny, module.CurrentWeather());
    }

    [Fact]
    public async Task Weather_StaleAfterOneHour_Hidden()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider();
        using var module = CreateClock(time, weather);
        await module.StartAsync(CancellationToken.None);
        weather.Respond = () => throw new WeatherException("hors ligne");

        // Une minute à la fois : les nouveaux essais (en échec) se déclenchent en route.
        for (var i = 0; i < 59; i++) time.Advance(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(Sunny, module.CurrentWeather());

        time.Advance(TimeSpan.FromSeconds(2)); // 1 h 00 min 01 s après la lecture
        Assert.Null(module.CurrentWeather());
        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
    }

    [Fact]
    public async Task Dispose_CancelsPendingFetch_AndStopsTimers()
    {
        var time = At(14, 32, 0);
        var pending = new TaskCompletionSource<WeatherNow>();
        var weather = new FakeWeatherProvider { Respond = () => pending.Task };
        var log = new FakeModuleLog();
        var module = CreateClock(time, weather, log: log);
        var events = 0;
        module.StatusChanged += (_, _) => events++;
        module.Invalidated += (_, _) => events++;
        await module.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(20)); // la lecture est toujours en cours : pas de seconde
        Assert.Equal(1, weather.Calls);
        var eventsBefore = events;

        module.Dispose();
        module.Dispose(); // idempotent
        Assert.True(weather.LastToken.IsCancellationRequested);

        pending.SetCanceled(weather.LastToken);
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, weather.Calls);
        Assert.Equal(eventsBefore, events);
        Assert.Empty(log.Entries);
        Assert.Equal(ModuleHealth.Online, module.Status.Health);
    }

    [Fact]
    public async Task Dispose_BeforeStart_StartDoesNothing()
    {
        var weather = new FakeWeatherProvider();
        var module = CreateClock(At(14, 32, 0), weather);
        module.Dispose();

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(0, weather.Calls);
    }

    [Fact]
    public void Layout_12hWithWeather_NoOverlap()
    {
        var bounds = new SKRect(0, 0, 600, 240);
        var weather = new ClockModule.WeatherText("12°", "Éclaircies");

        foreach (var time in new[] { "10:32 PM", "12:32 AM", "14:32" })
        {
            var layout = ClockModule.Layout(bounds, new GridSize(4, 2), SKTypeface.Default, 1.2f, time, "Samedi 24 octobre", weather);

            Assert.False(layout.Weather.IsEmpty);
            Assert.False(layout.Time.IntersectsWith(layout.Weather), $"{time} : l'heure {layout.Time} chevauche la météo {layout.Weather}");
            Assert.True(layout.Time.Right <= layout.Weather.Left - 24f + 0.5f, $"{time} : marge de 24 px non respectée");
            Assert.True(layout.Weather.Right <= bounds.Right - 22f + 0.01f);
            Assert.True(layout.Weather.Top >= bounds.Top);
        }
    }

    [Fact]
    public void Layout_DateNeverOverlapsWeather()
    {
        // Peu de hauteur : la date arrive à la hauteur du bloc météo et ne doit pas le croiser.
        var weather = new ClockModule.WeatherText("12°", "Pluie verglaçante");

        for (var width = 260f; width <= 460f; width += 20f)
            for (var height = 110f; height <= 200f; height += 10f)
            {
                var bounds = new SKRect(0, 0, width, height);
                var layout = ClockModule.Layout(bounds, new GridSize(3, 2), SKTypeface.Default, 1f, "14:32", "Mercredi 30 septembre", weather);

                Assert.False(layout.Date.IntersectsWith(layout.Weather), $"{width}x{height} : date {layout.Date} / météo {layout.Weather}");
                Assert.False(layout.Time.IntersectsWith(layout.Weather), $"{width}x{height} : heure {layout.Time} / météo {layout.Weather}");
            }
    }

    [Fact]
    public void Layout_WithoutWeather_UsesFullWidth()
    {
        var bounds = new SKRect(0, 0, 600, 240);

        var plain = ClockModule.Layout(bounds, new GridSize(4, 2), SKTypeface.Default, 1f, "14:32", "Samedi 24 octobre", null);
        var withWeather = ClockModule.Layout(bounds, new GridSize(4, 2), SKTypeface.Default, 1f, "14:32", "Samedi 24 octobre",
            new ClockModule.WeatherText("12°", "Éclaircies"));

        Assert.True(plain.Weather.IsEmpty);
        Assert.True(plain.Fit >= withWeather.Fit);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    public void Layout_OneRow_ShowsWeatherFromFourColumns(int columns, bool shown)
    {
        var bounds = new SKRect(0, 0, columns * 150f, 120);
        var weather = new ClockModule.WeatherText("12°", "Éclaircies");

        var layout = ClockModule.Layout(bounds, new GridSize(columns, 1), SKTypeface.Default, 1f, "14:32", "Samedi 24 octobre", weather);

        Assert.Equal(shown, !layout.Weather.IsEmpty);
        if (shown)
        {
            Assert.False(layout.Time.IntersectsWith(layout.Weather));
            Assert.Equal(bounds.Right - 22f, layout.Weather.Right, 0.01f); // l'icône touche le bord droit (marge)
        }
    }

    [Fact]
    public async Task Render_WithWeather_DrawsIconAtTopRight()
    {
        var time = At(14, 32, 0);
        using var module = CreateClock(time, new FakeWeatherProvider());
        await module.StartAsync(CancellationToken.None);

        using var bitmap = RenderTo(module, time.GetUtcNow(), 600, 240, new GridSize(4, 2));

        var cloud = new SKColor(0xE9, 0xEE, 0xF5);
        var sun = new SKColor(0xFF, 0xD2, 0x7A);
        Assert.True(CountPixels(bitmap, cloud, fromX: 400) > 100);
        Assert.True(CountPixels(bitmap, sun, fromX: 400) > 20);
        Assert.Equal(CountPixels(bitmap, cloud), CountPixels(bitmap, cloud, fromX: 400)); // rien à gauche : le bloc est à droite
    }

    [Fact]
    public async Task Render_WithoutLocation_DrawsNoIcon()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider();
        using var module = CreateClock(time, weather, "{}");
        await module.StartAsync(CancellationToken.None);

        using var bitmap = RenderTo(module, time.GetUtcNow(), 600, 240, new GridSize(4, 2));

        Assert.Equal(0, CountPixels(bitmap, new SKColor(0xE9, 0xEE, 0xF5)));
    }

    [Fact]
    public async Task Render_StaleWeather_DrawsNoIcon()
    {
        var time = At(14, 32, 0);
        var weather = new FakeWeatherProvider();
        using var module = CreateClock(time, weather);
        await module.StartAsync(CancellationToken.None);
        weather.Respond = () => throw new WeatherException("hors ligne");
        for (var i = 0; i < 62; i++) time.Advance(TimeSpan.FromMinutes(1));

        using var bitmap = RenderTo(module, time.GetUtcNow(), 600, 240, new GridSize(4, 2));

        Assert.Equal(0, CountPixels(bitmap, new SKColor(0xE9, 0xEE, 0xF5)));
    }

    [Fact]
    public async Task Render_OneRowThreeColumns_DrawsNoWeather()
    {
        var time = At(14, 32, 0);
        using var module = CreateClock(time, new FakeWeatherProvider());
        await module.StartAsync(CancellationToken.None);

        using var bitmap = RenderTo(module, time.GetUtcNow(), 450, 120, new GridSize(3, 1));

        Assert.Equal(0, CountPixels(bitmap, new SKColor(0xE9, 0xEE, 0xF5)));
    }

    [Fact]
    public async Task Render_OneRowFourColumns_DrawsSmallIcon()
    {
        var time = At(14, 32, 0);
        using var module = CreateClock(time, new FakeWeatherProvider());
        await module.StartAsync(CancellationToken.None);

        using var bitmap = RenderTo(module, time.GetUtcNow(), 600, 120, new GridSize(4, 1));

        Assert.True(CountPixels(bitmap, new SKColor(0xE9, 0xEE, 0xF5), fromX: 500) > 50);
    }

    [Theory]
    [InlineData(12.4, "12°")]
    [InlineData(12.5, "13°")]
    [InlineData(-0.5, "-1°")]
    [InlineData(-2.5, "-3°")]
    [InlineData(-0.2, "0°")]
    [InlineData(0, "0°")]
    [InlineData(98.6, "99°")]
    public void FormatTemperature_RoundsAwayFromZero_WithoutUnitLetter(double value, string expected)
    {
        Assert.Equal(expected, ClockModule.FormatTemperature(value));
    }
}
