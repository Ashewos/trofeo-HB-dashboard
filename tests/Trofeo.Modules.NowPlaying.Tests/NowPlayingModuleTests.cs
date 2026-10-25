using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.NowPlaying.Tests;

public class NowPlayingModuleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly SKColor Red = new(200, 30, 30);
    private static readonly SKColor Blue = new(30, 30, 200);

    private sealed class FakeSource : IMediaSource
    {
        public MediaSnapshot? Current { get; set; }
        public Exception? StartFailure { get; set; }
        public bool Disposed { get; private set; }
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public event EventHandler? Changed;

        public Task StartAsync(CancellationToken ct) =>
            StartFailure is null ? Task.CompletedTask : Task.FromException(StartFailure);

        public void Publish(MediaSnapshot? snapshot)
        {
            Current = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Disposed = true;
    }

    private static MediaSnapshot Playing(string title = "Blinding Lights", string artist = "The Weeknd", byte[]? art = null,
        bool playing = true, int position = 84, int duration = 200) =>
        new(title, artist, "Spotify", TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration), Start, playing, art);

    private static (NowPlayingModule Module, FakeModuleLog Log) Create(FakeSource source, TimeProvider time)
    {
        using var doc = JsonDocument.Parse("{}");
        var log = new FakeModuleLog();
        var context = new ModuleContext(Guid.NewGuid(), doc.RootElement.Clone(), time, log, new FakeSecretStore());
        return ((NowPlayingModule)new NowPlayingFactory(_ => source).Create(context), log);
    }

    private static FakeTimeProvider Clock() => new(Start);

    [Fact]
    public void Descriptor_MatchesBrief()
    {
        var d = new NowPlayingFactory(_ => new FakeSource()).Descriptor;

        Assert.Equal("nowplaying", d.TypeId);
        Assert.Equal("En écoute", d.DisplayName);
        Assert.Equal(new GridSize(3, 1), d.MinSize);
        Assert.Equal(new GridSize(3, 1), d.Presets[SizePreset.Compact]);
        Assert.Equal(new GridSize(4, 2), d.Presets[SizePreset.Ideal]);
        Assert.Equal(new GridSize(6, 2), d.Presets[SizePreset.Large]);
    }

    [Fact]
    public async Task Module_Playing_InvalidatesEverySecond_PausedDoesNot()
    {
        var time = Clock();
        var source = new FakeSource { Current = Playing() };
        var (module, _) = Create(source, time);
        using var _m = module;
        var invalidated = 0;
        module.Invalidated += (_, _) => invalidated++;
        await module.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(3, invalidated);

        source.Publish(Playing(playing: false));
        Assert.Equal(4, invalidated); // le changement lui-même
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(4, invalidated); // en pause : plus de minuteur

        source.Publish(Playing(playing: true));
        Assert.Equal(5, invalidated);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(7, invalidated); // la lecture reprend : le minuteur aussi

        source.Publish(null);
        Assert.Equal(8, invalidated);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(8, invalidated); // plus rien en lecture
    }

    [Fact]
    public async Task Module_PausedAtStart_HasNoTimer()
    {
        var time = Clock();
        var source = new FakeSource { Current = Playing(playing: false) };
        var (module, _) = Create(source, time);
        using var _m = module;
        var invalidated = 0;
        module.Invalidated += (_, _) => invalidated++;
        await module.StartAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, invalidated);
    }

    [Fact]
    public async Task Module_SourceFails_GoesOffline()
    {
        var source = new FakeSource { StartFailure = new InvalidOperationException("pas de gestionnaire") };
        var (module, log) = Create(source, Clock());
        using var _m = module;
        var changes = 0;
        module.StatusChanged += (_, _) => changes++;

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(ModuleHealth.Offline, module.Status.Health);
        Assert.Equal(1, changes);
        Assert.Contains(log.Entries, e => e.StartsWith("Error"));
    }

    [Fact]
    public async Task Module_NothingPlaying_StaysOnline()
    {
        var (module, _) = Create(new FakeSource(), Clock());
        using var _m = module;

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(ModuleHealth.Online, module.Status.Health);
    }

    [Fact]
    public async Task Dispose_ReleasesSourceAndStopsEvents_Idempotent()
    {
        var time = Clock();
        var source = new FakeSource { Current = Playing() };
        var (module, _) = Create(source, time);
        var raised = 0;
        module.Invalidated += (_, _) => raised++;
        await module.StartAsync(CancellationToken.None);

        module.Dispose();
        module.Dispose();
        time.Advance(TimeSpan.FromMinutes(1));
        source.Publish(Playing(title: "Autre"));

        Assert.True(source.Disposed);
        Assert.Equal(0, source.Subscribers);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Dispose_BeforeStart_NeverStartsSource()
    {
        var source = new FakeSource { StartFailure = new InvalidOperationException("ne doit pas être appelé") };
        var (module, log) = Create(source, Clock());
        module.Dispose();

        await module.StartAsync(CancellationToken.None);

        Assert.Equal(ModuleHealth.Online, module.Status.Health);
        Assert.Empty(log.Entries);
    }

    // --- Rendu ---

    private const int Margin = 30;
    private static readonly Theme Theme = Themes.Classique;

    private static SKBitmap Render(NowPlayingModule module, SKRectI rect, GridSize size, float scale = 1f, Theme? theme = null)
    {
        theme ??= Theme;
        var bitmap = new SKBitmap(GridGeometry.Width + 2 * Margin, GridGeometry.Height + 2 * Margin);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(theme.Background);
        var bounds = new SKRect(rect.Left + Margin, rect.Top + Margin, rect.Right + Margin, rect.Bottom + Margin);
        module.Render(canvas, new ModuleRenderContext(bounds, size, theme, scale, Start.AddSeconds(10)));
        return bitmap;
    }

    private static SKRectI Shifted(SKRectI rect) =>
        new(rect.Left + Margin, rect.Top + Margin, rect.Right + Margin, rect.Bottom + Margin);

    private static int CountOutside(SKBitmap bitmap, SKRectI inside)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (x >= inside.Left && x < inside.Right && y >= inside.Top && y < inside.Bottom) continue;
                if (bitmap.GetPixel(x, y) != Theme.Background) count++;
            }
        return count;
    }

    private static SKRectI? InkBounds(SKBitmap bitmap, SKRectI area)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                if (bitmap.GetPixel(x, y) == Theme.Background) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        return right < 0 ? null : new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static byte[] Png(SKColor color, int width = 64, int height = 64)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task Render_Nothing_DrawsRienEnLecture()
    {
        var (module, _) = Create(new FakeSource(), Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);
        using var bitmap = Render(module, rect, new GridSize(4, 2));
        var inside = Shifted(rect);

        using var font = new SKFont(Theme.Typeface, 24f);
        var expected = ModuleDrawing.MeasureText("Rien en lecture", font);
        var ink = InkBounds(bitmap, inside);

        Assert.NotNull(ink);
        Assert.InRange(ink.Value.Width, expected - 4, expected + 4);
        Assert.InRange(ink.Value.MidX, inside.MidX - 3, inside.MidX + 3);
        Assert.InRange(ink.Value.MidY, inside.MidY - 8, inside.MidY + 8);
        Assert.Equal(0, CountOutside(bitmap, inside));
    }

    [Fact]
    public async Task Render_Playing_DrawsCoverAndText()
    {
        var source = new FakeSource { Current = Playing(art: Png(Red)) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);
        using var bitmap = Render(module, rect, new GridSize(4, 2));
        var inside = Shifted(rect);

        // Pochette carrée à gauche : la hauteur du contenu (18 px de marge en haut et en bas).
        var side = inside.Height - 36;
        Assert.Equal(Red, bitmap.GetPixel(inside.Left + 18 + side / 2, inside.MidY));
        Assert.Equal(Theme.Background, bitmap.GetPixel(inside.Left + 18 + side + 2, inside.MidY - 5));
        // Le texte est à droite de la pochette.
        var right = new SKRectI(inside.Left + 18 + side + 10, inside.Top, inside.Right, inside.Bottom);
        Assert.NotNull(InkBounds(bitmap, right));
        Assert.Equal(0, CountOutside(bitmap, inside));
    }

    [Fact]
    public async Task Render_Playing_ProgressBarGrowsWithTime()
    {
        var source = new FakeSource { Current = Playing(position: 0, duration: 100) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);

        int AccentPixels(DateTimeOffset now)
        {
            using var bitmap = new SKBitmap(GridGeometry.Width, GridGeometry.Height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(Theme.Background);
            module.Render(canvas, new ModuleRenderContext(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
                new GridSize(4, 2), Theme, 1f, now));
            var count = 0;
            for (var y = rect.Top; y < rect.Bottom; y++)
                for (var x = rect.Left; x < rect.Right; x++)
                    if (bitmap.GetPixel(x, y) == Theme.Accent) count++;
            return count;
        }

        var early = AccentPixels(Start.AddSeconds(5));
        var late = AccentPixels(Start.AddSeconds(80));

        Assert.True(early > 0);
        Assert.True(late > early * 5, $"{early} → {late}");
    }

    [Fact]
    public async Task Render_NoArtwork_DrawsMutedSquare()
    {
        var source = new FakeSource { Current = Playing(art: null) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);
        using var bitmap = Render(module, rect, new GridSize(4, 2));
        var inside = Shifted(rect);

        var pixel = bitmap.GetPixel(inside.Left + 18 + 40, inside.MidY);

        Assert.NotEqual(Theme.Background, pixel);
        Assert.NotEqual(Red, pixel);
    }

    [Fact]
    public async Task Render_UndecodableArtwork_FallsBackToPlaceholder()
    {
        var source = new FakeSource { Current = Playing(art: [1, 2, 3, 4, 5]) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);
        var inside = Shifted(rect);

        using var first = Render(module, rect, new GridSize(4, 2));
        using var second = Render(module, rect, new GridSize(4, 2)); // l'échec est mis en cache : pas de nouveau décodage

        Assert.NotEqual(Theme.Background, first.GetPixel(inside.Left + 18 + 40, inside.MidY));
        Assert.Equal(first.GetPixel(inside.Left + 18 + 40, inside.MidY), second.GetPixel(inside.Left + 18 + 40, inside.MidY));
        Assert.Equal(0, CountOutside(first, inside));
    }

    [Fact]
    public async Task Render_ArtworkChanges_NewImageDrawn_RenderAfterDisposeIsSafe()
    {
        var source = new FakeSource { Current = Playing(art: Png(Red)) };
        var (module, _) = Create(source, Clock());
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);
        var probe = (X: Shifted(rect).Left + 58, Y: Shifted(rect).MidY);

        using (var first = Render(module, rect, new GridSize(4, 2)))
            Assert.Equal(Red, first.GetPixel(probe.X, probe.Y));

        source.Publish(Playing(title: "Autre", art: Png(Blue)));
        using (var second = Render(module, rect, new GridSize(4, 2)))
            Assert.Equal(Blue, second.GetPixel(probe.X, probe.Y));

        module.Dispose();
        using var after = Render(module, rect, new GridSize(4, 2)); // ne lève pas et ne redécode rien
        Assert.NotEqual(Blue, after.GetPixel(probe.X, probe.Y));
    }

    [Theory]
    [InlineData(0.85f)]
    [InlineData(1.0f)]
    [InlineData(1.2f)]
    public async Task Render_LongJapaneseTitle_StaysInside(float scale)
    {
        var title = string.Concat(Enumerable.Repeat("とても長い日本語のタイトル🎵", 6));
        var artist = string.Concat(Enumerable.Repeat("アーティスト名前 ", 8));
        var source = new FakeSource { Current = Playing(title, artist, Png(new SKColor(10, 160, 90), 300, 200)) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);

        foreach (var (col, row, cols, rows) in new[] { (8, 0, 4, 2), (5, 1, 6, 2), (0, 3, 3, 1), (13, 3, 3, 1) })
        {
            var rect = GridGeometry.ModuleRect(col, row, cols, rows);
            using var bitmap = Render(module, rect, new GridSize(cols, rows), scale);

            Assert.Equal(0, CountOutside(bitmap, Shifted(rect)));
            Assert.NotNull(InkBounds(bitmap, Shifted(rect)));
        }
    }

    [Fact]
    public async Task Render_Compact_HasNoCover_ShowsTitleAndArtist()
    {
        var source = new FakeSource { Current = Playing(art: Png(Red)) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(0, 3, 3, 1);
        using var bitmap = Render(module, rect, new GridSize(3, 1));
        var inside = Shifted(rect);

        for (var y = inside.Top; y < inside.Bottom; y++)
            for (var x = inside.Left; x < inside.Right; x++)
                Assert.NotEqual(Red, bitmap.GetPixel(x, y));
        Assert.NotNull(InkBounds(bitmap, inside));
        Assert.Equal(0, CountOutside(bitmap, inside));
    }

    // --- Thème : barre de progression ---

    private static readonly SKColor TestTrack = new(0x00, 0x80, 0x00);

    private static (int Count, int Top, int Bottom) TrackPixels(SKBitmap bitmap)
    {
        int count = 0, top = int.MaxValue, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y) == TestTrack)
                {
                    count++;
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
        return (count, top, bottom);
    }

    [Fact]
    public async Task NowPlaying_ProgressUsesThemeTrack()
    {
        var source = new FakeSource { Current = Playing(position: 10, duration: 200) };
        var (module, _) = Create(source, Clock());
        using var _m = module;
        await module.StartAsync(CancellationToken.None);
        var rect = GridGeometry.ModuleRect(8, 0, 4, 2);

        using var rounded = Render(module, rect, new GridSize(4, 2), theme: Theme with { Track = TestTrack });
        using var pixel = Render(module, rect, new GridSize(4, 2), theme: Theme with { Track = TestTrack, Shape = ThemeShape.Pixel });

        // Barre arrondie : la piste du thème, 8 px de haut (extrémités anticrénelées).
        var (roundedCount, roundedTop, roundedBottom) = TrackPixels(rounded);
        Assert.True(roundedCount > 1000, $"{roundedCount} pixels de piste");
        Assert.InRange(roundedBottom - roundedTop + 1, 6, 8);

        // Barre Pixel : 12 px de haut, en segments (la piste est coupée de vides sur la ligne du milieu).
        var (pixelCount, pixelTop, pixelBottom) = TrackPixels(pixel);
        Assert.True(pixelCount > 1000, $"{pixelCount} pixels de piste");
        Assert.Equal(12, pixelBottom - pixelTop + 1);
        var middle = (pixelTop + pixelBottom) / 2;
        var runs = 0;
        var inside = false;
        for (var x = 0; x < pixel.Width; x++)
        {
            var c = pixel.GetPixel(x, middle);
            var on = c == TestTrack || c == Theme.Accent;
            if (on && !inside) runs++;
            inside = on;
        }

        Assert.True(runs > 5, $"{runs} segments seulement");
    }
}
