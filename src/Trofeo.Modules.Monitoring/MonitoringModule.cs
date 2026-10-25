using SkiaSharp;
using Trofeo.ModuleKit;
using Trofeo.Sensors;

namespace Trofeo.Modules.Monitoring;

/// <summary>
/// Lignes de monitoring (CPU, GPU, RAM…) avec barre et valeurs, redessinées à chaque relevé de la source de capteurs.
/// Passe hors ligne si aucun relevé n'arrive pendant 5 s.
/// </summary>
public sealed class MonitoringModule : IModule
{
    /// <summary>Délai sans relevé au bout duquel le module est hors ligne.</summary>
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HealthPeriod = TimeSpan.FromSeconds(1);

    internal const string DriverHint = "Temp. CPU : installe le pilote";

    // Dimensions des colonnes d'une ligne, avant multiplication par l'échelle de texte.
    private const float LabelSize = 22f;
    private const float ValueSize = 30f;
    private const float UnitSize = 18f;
    private const float BarHeight = 8f;
    private const float LabelWidth = 64f;
    private const float ValueWidth = 86f;
    private const float SecondaryWidth = 96f;
    private const float ColumnGap = 12f;

    /// <summary>À partir de cette fraction la barre passe en <see cref="Theme.Warning"/>.</summary>
    private const float WarnFraction = 0.9f;

    /// <summary>Opacité de la piste des barres : environ 8 % du texte.</summary>
    private const byte TrackAlpha = 0x14;

    private readonly MonitoringSettings _settings;
    private readonly ISensorFeed _feed;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private IDisposable? _ticket;
    private ITimer? _timer;
    private bool _started;
    private bool _disposed;
    private long _lastUpdateTicks;
    private ModuleStatus _status = new(ModuleHealth.Online);

    public MonitoringModule(ModuleDescriptor descriptor, MonitoringSettings settings, ISensorFeed feed, TimeProvider time)
    {
        Descriptor = descriptor;
        _settings = settings;
        _feed = feed;
        _time = time;
    }

    public ModuleDescriptor Descriptor { get; }
    public ModuleStatus Status => Volatile.Read(ref _status);
    public bool IsAnimating => false;

    public event EventHandler? Invalidated;
    public event EventHandler? StatusChanged;

    public Task StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed || _started) return Task.CompletedTask;
            _started = true;
            Volatile.Write(ref _lastUpdateTicks, _time.GetUtcNow().UtcTicks);
        }

        // Abonnement puis acquisition hors du verrou : la source peut lever Updated pendant Acquire.
        _feed.Updated += OnUpdated;
        var ticket = _feed.Acquire();
        var timer = _time.CreateTimer(_ => OnHealthTick(), null, HealthPeriod, HealthPeriod);

        var disposedMeanwhile = false;
        lock (_gate)
        {
            if (_disposed) disposedMeanwhile = true;
            else
            {
                _ticket = ticket;
                _timer = timer;
            }
        }

        if (disposedMeanwhile)
        {
            _feed.Updated -= OnUpdated;
            timer.Dispose();
            ticket.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var snapshot = _feed.Latest;
        var scale = ctx.TextScale;
        var theme = ctx.Theme;
        var bounds = ctx.Bounds;

        var left = bounds.Left + MonitoringRows.PaddingX * scale;
        var right = bounds.Right - MonitoringRows.PaddingX * scale;
        var width = right - left;
        if (width <= 0f) return;

        var rowIds = VisibleRows(bounds, scale);
        DrawTitle(canvas, ctx, snapshot, rowIds.Count, left, width);
        if (rowIds.Count == 0) return;

        using var labelFont = new SKFont(theme.Typeface, LabelSize * scale);
        using var valueFont = new SKFont(theme.Typeface, ValueSize * scale);
        using var unitFont = new SKFont(theme.Typeface, UnitSize * scale);
        using var mutedPaint = new SKPaint { Color = theme.TextMuted, IsAntialias = true };
        using var textPaint = new SKPaint { Color = theme.Text, IsAntialias = true };

        // Colonnes de droite à gauche. Si la largeur ne suffit pas pour les trois colonnes de texte, on les
        // resserre proportionnellement (la barre disparaît) plutôt que de les laisser se chevaucher.
        var natural = (LabelWidth + ValueWidth + SecondaryWidth + 3 * ColumnGap) * scale;
        var fit = width < natural ? width / natural : 1f;
        var gap = ColumnGap * scale * fit;
        var labelRight = left + LabelWidth * scale * fit;
        var secondaryLeft = right - SecondaryWidth * scale * fit;
        var valueRight = secondaryLeft - gap;
        var valueLeft = valueRight - ValueWidth * scale * fit;
        var barLeft = labelRight + gap;
        var barRight = valueLeft - gap;

        var rowHeight = MonitoringRows.RowHeight * scale;
        var rowGap = MonitoringRows.RowGap * scale;
        var bottom = bounds.Bottom - MonitoringRows.PaddingY * scale;
        var track = theme.Text.WithAlpha(TrackAlpha);

        for (var i = 0; i < rowIds.Count; i++)
        {
            var row = MonitoringRows.Build(rowIds[i], snapshot);
            var remaining = rowIds.Count - i;
            var top = bottom - remaining * rowHeight - (remaining - 1) * rowGap;
            var mid = top + rowHeight / 2f;

            DrawCentered(canvas, row.Label, left, mid, SKTextAlign.Left, labelFont, mutedPaint, labelRight - left);

            if (row.Bar is { } fraction && barRight > barLeft)
            {
                var barHeight = BarHeight * scale;
                var rect = new SKRect(barLeft, mid - barHeight / 2f, barRight, mid + barHeight / 2f);
                ModuleDrawing.DrawBar(canvas, rect, fraction, fraction >= WarnFraction ? theme.Warning : theme.Accent, track);
            }

            // Sans barre, la valeur peut s'étendre sur la place qu'elle aurait prise (« 1450 tr/min »).
            var valueMax = row.Bar is null ? valueRight - barLeft : valueRight - valueLeft;
            DrawCentered(canvas, row.Value, valueRight, mid, SKTextAlign.Right, valueFont, textPaint, valueMax);

            if (row.Secondary is { } secondary)
            {
                var isUnit = secondary.StartsWith('/');
                DrawCentered(canvas, secondary, right, mid, SKTextAlign.Right,
                    isUnit ? unitFont : valueFont, isUnit ? mutedPaint : textPaint, right - secondaryLeft);
            }
        }
    }

    public void Dispose()
    {
        IDisposable? ticket;
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ticket = _ticket;
            timer = _timer;
            _ticket = null;
            _timer = null;
        }

        _feed.Updated -= OnUpdated;
        timer?.Dispose();
        ticket?.Dispose();
    }

    /// <summary>Titre de la première ligne : l'indice du pilote si besoin, sinon le titre choisi ou le nom du matériel.</summary>
    /// <param name="drawnRows">Nombre de lignes réellement dessinées (les premières des réglages).</param>
    internal string CurrentTitle(int drawnRows = int.MaxValue) => TitleLine(_feed.Latest, drawnRows).Text;

    /// <summary>Les lignes qui tiennent dans <paramref name="bounds"/> : les premières des réglages.</summary>
    internal IReadOnlyList<string> VisibleRows(SKRect bounds, float textScale) =>
        _settings.Rows.Take(MonitoringRows.Capacity(bounds, textScale)).ToList();

    private (string Text, bool IsWarning) TitleLine(SensorSnapshot? snapshot, int drawnRows)
    {
        // Sans relevé on ne sait rien des capteurs : pas d'indice. Il ne concerne que la ligne CPU, donc seulement
        // si elle est affichée. Il disparaît si l'assistant élevé tourne (Elevated) : le pilote est déjà là, et
        // l'indice ne ferait que répéter ce que l'utilisateur a déjà fait.
        if (snapshot is not null
            && !snapshot.Elevated
            && !snapshot.Values.ContainsKey(SensorKeys.CpuTemp)
            && _settings.Rows.Take(Math.Max(0, drawnRows)).Contains("cpu"))
        {
            return (DriverHint, true);
        }

        return (_settings.Title ?? HardwareNames.Short(snapshot?.CpuName, snapshot?.GpuName), false);
    }

    private void DrawTitle(SKCanvas canvas, ModuleRenderContext ctx, SensorSnapshot? snapshot, int drawnRows, float left, float width)
    {
        var scale = ctx.TextScale;
        var (text, isWarning) = TitleLine(snapshot, drawnRows);
        using var font = new SKFont(ctx.Theme.Typeface, MonitoringRows.TitleSize * scale);
        using var paint = new SKPaint { Color = isWarning ? ctx.Theme.Warning : ctx.Theme.TextMuted, IsAntialias = true };
        var mid = ctx.Bounds.Top + (MonitoringRows.PaddingY + MonitoringRows.TitleSize / 2f) * scale;
        DrawCentered(canvas, text, left, mid, SKTextAlign.Left, font, paint, width);
    }

    /// <summary>Dessine le texte (ellipsé à <paramref name="maxWidth"/>) centré verticalement sur <paramref name="mid"/>.</summary>
    private static void DrawCentered(SKCanvas canvas, string text, float x, float mid, SKTextAlign align, SKFont font, SKPaint paint, float maxWidth)
    {
        if (maxWidth <= 0f) return;
        var metrics = font.Metrics;
        var baseline = mid - (metrics.Ascent + metrics.Descent) / 2f;
        ModuleDrawing.DrawText(canvas, ModuleDrawing.Ellipsize(text, font, maxWidth), x, baseline, align, font, paint);
    }

    private void OnUpdated(object? sender, EventArgs e)
    {
        bool backOnline;
        lock (_gate)
        {
            if (_disposed) return;
            Volatile.Write(ref _lastUpdateTicks, _time.GetUtcNow().UtcTicks);
            backOnline = Transition(ModuleHealth.Online);
        }

        if (backOnline) StatusChanged?.Invoke(this, EventArgs.Empty);
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnHealthTick()
    {
        bool wentOffline;
        lock (_gate)
        {
            if (_disposed) return;
            var silence = TimeSpan.FromTicks(_time.GetUtcNow().UtcTicks - Volatile.Read(ref _lastUpdateTicks));
            wentOffline = silence >= OfflineAfter && Transition(ModuleHealth.Offline);
        }

        if (wentOffline) StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Change l'état s'il diffère ; à appeler sous <see cref="_gate"/>. Renvoie vrai s'il a changé.</summary>
    private bool Transition(ModuleHealth health)
    {
        if (_status.Health == health) return false;
        Volatile.Write(ref _status, new ModuleStatus(health));
        return true;
    }
}
