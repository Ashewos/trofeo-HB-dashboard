using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.NowPlaying;

/// <summary>
/// Ce qui est en lecture : pochette, titre, artiste, application et progression, d'après une <see cref="IMediaSource"/>.
/// Redessiné à chaque changement de la source, et chaque seconde tant que la lecture est en cours (progression).
/// </summary>
public sealed class NowPlayingModule : IModule
{
    private static readonly TimeSpan ProgressPeriod = TimeSpan.FromSeconds(1);

    internal const string NothingPlaying = "Rien en lecture";

    // Dimensions de la mise en page, avant multiplication par l'échelle de texte.
    private const float Padding = 18f;
    private const float CompactPaddingX = 22f;
    private const float CoverRadius = 10f;
    private const float CoverGap = 18f;
    private const float LineGap = 8f;
    private const float IconSize = 14f;
    private const float IconGap = 8f;
    private const float AppSize = 18f;
    private const float TitleSize = 34f;
    private const float ArtistSize = 22f;
    private const float BarHeight = 8f;
    private const float TimeSize = 16f;
    private const float CompactTitleSize = 26f;
    private const float CompactArtistSize = 18f;
    private const float CompactGap = 4f;
    private const float EmptySize = 24f;

    /// <summary>Opacité du carré qui remplace une pochette absente : 25 %.</summary>
    private const byte PlaceholderAlpha = 0x40;

    private readonly IMediaSource _source;
    private readonly TimeProvider _time;
    private readonly IModuleLog _log;
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _started;
    private bool _disposed;
    private ModuleStatus _status = new(ModuleHealth.Online);

    // Cache de la pochette décodée. Le décodage et le dessin ont lieu sur le thread de rendu, mais Dispose peut
    // venir d'un autre thread : tout accès à l'image se fait sous _artGate, et Dispose attend la fin d'un dessin en
    // cours avant de libérer l'image. Après Dispose, plus rien n'est décodé ni mis en cache.
    private readonly object _artGate = new();
    private byte[]? _artKey;
    private SKImage? _artImage;
    private bool _artDisposed;

    public NowPlayingModule(ModuleDescriptor descriptor, IMediaSource source, TimeProvider time, IModuleLog log)
    {
        Descriptor = descriptor;
        _source = source;
        _time = time;
        _log = log;
    }

    public ModuleDescriptor Descriptor { get; }
    public ModuleStatus Status => Volatile.Read(ref _status);
    public bool IsAnimating => false;

    public event EventHandler? Invalidated;
    public event EventHandler? StatusChanged;

    public async Task StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
        }

        _source.Changed += OnChanged;
        try
        {
            await _source.StartAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Contrôles média de Windows indisponibles : {ex.Message}");
            bool changed;
            lock (_gate)
            {
                changed = !_disposed && Transition(ModuleHealth.Offline);
            }
            if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // La source a pu publier avant l'abonnement ou pendant le démarrage : on s'aligne sur son état courant.
        SyncTimer();
    }

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var bounds = ctx.Bounds;
        if (bounds.Width <= 0f || bounds.Height <= 0f) return;

        var snapshot = _source.Current;
        canvas.Save();
        canvas.ClipRect(bounds);
        try
        {
            if (snapshot is null)
            {
                ReleaseArtwork();
                DrawNothing(canvas, ctx);
            }
            else if (ctx.Size.Rows < 2)
            {
                ReleaseArtwork();
                DrawCompact(canvas, ctx, snapshot);
            }
            else
            {
                DrawFull(canvas, ctx, snapshot);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        _source.Changed -= OnChanged;
        timer?.Dispose();
        _source.Dispose();

        lock (_artGate)
        {
            _artDisposed = true;
            _artImage?.Dispose();
            _artImage = null;
            _artKey = null;
        }
    }

    // --- Cycle de vie ---

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!SyncTimer()) return;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick()
    {
        if (Volatile.Read(ref _disposed)) return;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Le minuteur de progression tourne tant que la lecture est en cours. Faux si le module est libéré.</summary>
    private bool SyncTimer()
    {
        var playing = _source.Current?.IsPlaying == true;
        ITimer? stopped = null;
        lock (_gate)
        {
            if (_disposed) return false;
            if (playing && _timer is null)
            {
                _timer = _time.CreateTimer(_ => OnTick(), null, ProgressPeriod, ProgressPeriod);
            }
            else if (!playing && _timer is not null)
            {
                stopped = _timer;
                _timer = null;
            }
        }

        stopped?.Dispose();
        return true;
    }

    /// <summary>Change l'état s'il diffère ; à appeler sous <see cref="_gate"/>. Renvoie vrai s'il a changé.</summary>
    private bool Transition(ModuleHealth health)
    {
        if (_status.Health == health) return false;
        Volatile.Write(ref _status, new ModuleStatus(health));
        return true;
    }

    // --- Dessin ---

    private static void DrawNothing(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var scale = ctx.TextScale;
        using var font = ModuleDrawing.Font(ctx.Theme, EmptySize * scale);
        using var paint = new SKPaint { Color = ctx.Theme.TextMuted, IsAntialias = true };
        var maxWidth = ctx.Bounds.Width - 2 * Padding * scale;
        DrawLine(canvas, NothingPlaying, ctx.Bounds.MidX, ctx.Bounds.MidY, SKTextAlign.Center, font, paint, maxWidth);
    }

    /// <summary>3×1 : le titre et l'artiste seulement, centrés verticalement.</summary>
    private static void DrawCompact(SKCanvas canvas, ModuleRenderContext ctx, MediaSnapshot snapshot)
    {
        var scale = ctx.TextScale;
        var theme = ctx.Theme;
        var left = ctx.Bounds.Left + CompactPaddingX * scale;
        var width = ctx.Bounds.Width - 2 * CompactPaddingX * scale;
        if (width <= 0f) return;

        var titleHeight = CompactTitleSize * scale;
        var artistHeight = CompactArtistSize * scale;
        var top = ctx.Bounds.MidY - (titleHeight + CompactGap * scale + artistHeight) / 2f;

        using var titleFont = ModuleDrawing.Font(theme, titleHeight);
        using var artistFont = ModuleDrawing.Font(theme, artistHeight);
        using var textPaint = new SKPaint { Color = theme.Text, IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = theme.TextMuted, IsAntialias = true };

        DrawLine(canvas, snapshot.Title, left, top + titleHeight / 2f, SKTextAlign.Left, titleFont, textPaint, width);
        DrawLine(canvas, snapshot.Artist, left, top + titleHeight + CompactGap * scale + artistHeight / 2f,
            SKTextAlign.Left, artistFont, mutedPaint, width);
    }

    /// <summary>4×2 et plus : pochette carrée à gauche, application, titre, artiste, barre et temps à droite.</summary>
    private void DrawFull(SKCanvas canvas, ModuleRenderContext ctx, MediaSnapshot snapshot)
    {
        var scale = ctx.TextScale;
        var theme = ctx.Theme;
        var content = SKRect.Inflate(ctx.Bounds, -Padding * scale, -Padding * scale);
        if (content.Width <= 0f || content.Height <= 0f) return;

        // La pochette a la hauteur du contenu, sans dépasser la moitié de la largeur pour laisser de la place au texte.
        var side = Math.Min(content.Height, content.Width / 2f);
        var cover = SKRect.Create(content.Left, content.MidY - side / 2f, side, side);
        DrawCover(canvas, cover, snapshot.Artwork, theme, scale);

        var left = cover.Right + CoverGap * scale;
        var width = content.Right - left;
        if (width <= 0f) return;

        var gap = LineGap * scale;
        var appHeight = AppSize * scale;
        var titleHeight = TitleSize * scale;
        var artistHeight = ArtistSize * scale;
        var barHeight = BarHeight * scale;
        var timeHeight = TimeSize * scale;
        var total = appHeight + titleHeight + artistHeight + barHeight + timeHeight + 4 * gap;
        var y = content.MidY - total / 2f;

        using var appFont = ModuleDrawing.Font(theme, appHeight);
        using var titleFont = ModuleDrawing.Font(theme, titleHeight);
        using var artistFont = ModuleDrawing.Font(theme, artistHeight);
        using var timeFont = ModuleDrawing.Font(theme, timeHeight);
        using var textPaint = new SKPaint { Color = theme.Text, IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = theme.TextMuted, IsAntialias = true };

        // Application, précédée du symbole lecture ou pause.
        var icon = IconSize * scale;
        DrawStateIcon(canvas, snapshot.IsPlaying, left, y + appHeight / 2f, icon, mutedPaint);
        var appLeft = left + icon + IconGap * scale;
        DrawLine(canvas, snapshot.AppName, appLeft, y + appHeight / 2f, SKTextAlign.Left, appFont, mutedPaint, left + width - appLeft);
        y += appHeight + gap;

        DrawLine(canvas, snapshot.Title, left, y + titleHeight / 2f, SKTextAlign.Left, titleFont, textPaint, width);
        y += titleHeight + gap;

        DrawLine(canvas, snapshot.Artist, left, y + artistHeight / 2f, SKTextAlign.Left, artistFont, mutedPaint, width);
        y += artistHeight + gap;

        var position = MediaTimeline.Current(snapshot, ctx.Now);
        var fraction = snapshot.Duration > TimeSpan.Zero ? (float)(position.TotalSeconds / snapshot.Duration.TotalSeconds) : 0f;
        ModuleDrawing.DrawBar(canvas, ModuleDrawing.BarRect(new SKRect(left, y, left + width, y + barHeight), theme), fraction,
            theme.Accent, theme);
        y += barHeight + gap;

        // Durée connue : temps écoulé à gauche et durée totale à droite ; sinon (direct) le temps écoulé seul.
        var mid = y + timeHeight / 2f;
        var elapsed = MediaTimeline.Format(position);
        if (snapshot.Duration > TimeSpan.Zero)
        {
            var totalText = MediaTimeline.Format(snapshot.Duration);
            var totalWidth = ModuleDrawing.MeasureText(totalText, timeFont);
            DrawLine(canvas, totalText, left + width, mid, SKTextAlign.Right, timeFont, mutedPaint, width);
            DrawLine(canvas, elapsed, left, mid, SKTextAlign.Left, timeFont, mutedPaint, Math.Max(0f, width - totalWidth - gap));
        }
        else
        {
            DrawLine(canvas, elapsed, left, mid, SKTextAlign.Left, timeFont, mutedPaint, width);
        }
    }

    /// <summary>Dessine la pochette (rognée au centre pour remplir le carré), ou un carré <c>TextMuted</c> à 25 % si elle manque ou ne se décode pas.</summary>
    private void DrawCover(SKCanvas canvas, SKRect rect, byte[]? artwork, Theme theme, float scale)
    {
        var radius = CoverRadius * scale;
        using var rounded = new SKRoundRect(rect, radius, radius);

        lock (_artGate)
        {
            var image = ArtworkImage(artwork);
            if (image is not null)
            {
                canvas.Save();
                canvas.ClipRoundRect(rounded, SKClipOperation.Intersect, antialias: true);
                canvas.DrawImage(image, CenterSquare(image), rect,
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
                canvas.Restore();
                return;
            }
        }

        using var paint = new SKPaint { Color = theme.TextMuted.WithAlpha(PlaceholderAlpha), IsAntialias = true };
        canvas.DrawRoundRect(rounded, paint);
    }

    /// <summary>L'image décodée de <paramref name="artwork"/> (mise en cache par référence du tableau), ou <c>null</c>. À appeler sous <see cref="_artGate"/>.</summary>
    private SKImage? ArtworkImage(byte[]? artwork)
    {
        if (_artDisposed) return null;
        if (!ReferenceEquals(artwork, _artKey))
        {
            _artImage?.Dispose();
            _artImage = artwork is null ? null : TryDecode(artwork);
            _artKey = artwork;
        }
        return _artImage;
    }

    /// <summary>Libère la pochette en cache quand elle n'est plus affichée.</summary>
    private void ReleaseArtwork()
    {
        lock (_artGate)
        {
            _artImage?.Dispose();
            _artImage = null;
            _artKey = null;
        }
    }

    private static SKImage? TryDecode(byte[] bytes)
    {
        try
        {
            return SKImage.FromEncodedData(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SKRect CenterSquare(SKImage image)
    {
        var side = Math.Min(image.Width, image.Height);
        return SKRect.Create((image.Width - side) / 2f, (image.Height - side) / 2f, side, side);
    }

    /// <summary>Triangle (lecture) ou deux barres (pause) dans un carré de <paramref name="size"/> centré verticalement sur <paramref name="mid"/>.</summary>
    private static void DrawStateIcon(SKCanvas canvas, bool playing, float left, float mid, float size, SKPaint paint)
    {
        var top = mid - size / 2f;
        if (playing)
        {
            using var path = new SKPath();
            path.MoveTo(left, top);
            path.LineTo(left + size, mid);
            path.LineTo(left, top + size);
            path.Close();
            canvas.DrawPath(path, paint);
            return;
        }

        var bar = size * 0.3f;
        canvas.DrawRect(new SKRect(left + size * 0.1f, top, left + size * 0.1f + bar, top + size), paint);
        canvas.DrawRect(new SKRect(left + size * 0.6f, top, left + size * 0.6f + bar, top + size), paint);
    }

    /// <summary>Dessine le texte (ellipsé à <paramref name="maxWidth"/>) centré verticalement sur <paramref name="mid"/>.</summary>
    private static void DrawLine(SKCanvas canvas, string text, float x, float mid, SKTextAlign align, SKFont font, SKPaint paint, float maxWidth)
    {
        if (maxWidth <= 0f || string.IsNullOrEmpty(text)) return;
        var metrics = font.Metrics;
        var baseline = mid - (metrics.Ascent + metrics.Descent) / 2f;
        ModuleDrawing.DrawText(canvas, ModuleDrawing.Ellipsize(text, font, maxWidth), x, baseline, align, font, paint);
    }
}
