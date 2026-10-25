using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>
/// Débit réseau descendant et montant d'une <see cref="IThroughputSource"/>, avec la courbe des trois dernières
/// minutes. La source est lue puis relue après <see cref="IThroughputSource.Interval"/> (relu à chaque fois, la source
/// peut le changer), sans lectures qui se chevauchent, y compris avec une lecture abandonnée sur délai qui court
/// encore ; le module la libère avec lui.
/// </summary>
public sealed class NetworkModule : IModule
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(3);

    /// <summary>Échelle minimale de la courbe : 1 Mb/s, soit 125 000 octets par seconde.</summary>
    private const double MinScaleBytesPerSecond = 125_000d;

    // Dimensions de la mise en page, avant multiplication par l'échelle de texte.
    private const float Padding = 18f;
    private const float CompactPaddingY = 10f;
    private const float TitleSize = 18f;
    private const float ValueSize = 44f;
    private const float UnitSize = 16f;
    private const float CurveHeight = 62f;
    private const float MinCurveHeight = 24f;
    private const float SectionGap = 10f;
    private const float ColumnGap = 16f;
    private const float ArrowWidth = 20f;
    private const float ArrowHeight = 18f;
    private const float ArrowGap = 8f;
    private const float UnitGap = 6f;
    private const float DownStroke = 2f;
    private const float UpStroke = 1.5f;

    private const string NoValue = "—";

    /// <summary>Durée minimale accordée à une lecture ; au-delà elle vaut trois fois l'intervalle de la source.</summary>
    private static readonly TimeSpan MinReadTimeout = TimeSpan.FromSeconds(10);

    private readonly IThroughputSource _source;
    private readonly TimeProvider _time;
    private readonly IModuleLog _log;
    private readonly ThroughputHistory _history = new(HistoryWindow);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _started;
    private bool _disposed;
    private int _reading;
    private ModuleStatus _status = new(ModuleHealth.Online);
    private ThroughputSample? _latest;

    public NetworkModule(ModuleDescriptor descriptor, IThroughputSource source, TimeProvider time, IModuleLog log)
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

    /// <summary>Dernier échantillon affiché, ou <c>null</c> (rien lu, ou dernière lecture ratée).</summary>
    internal ThroughputSample? LatestSample => Volatile.Read(ref _latest);

    /// <summary>Copie de l'historique des trois dernières minutes.</summary>
    internal IReadOnlyList<(DateTimeOffset At, ThroughputSample Sample)> HistorySnapshot() => _history.Snapshot();

    public async Task StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
        }

        // Minuteur à un coup, réarmé à la fin de chaque lecture avec l'intervalle que la source annonce alors.
        var timer = _time.CreateTimer(_ => OnTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var disposedMeanwhile = false;
        lock (_gate)
        {
            if (_disposed) disposedMeanwhile = true;
            else _timer = timer;
        }
        if (disposedMeanwhile)
        {
            timer.Dispose();
            return;
        }

        // Première lecture tout de suite : l'écran n'attend pas un Interval pour afficher un état.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        await ReadOnceAsync(linked.Token).ConfigureAwait(false);
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

        // Le jeton n'est pas libéré : sans minuteur interne il ne retient rien, et une lecture en cours peut encore le lire.
        _stop.Cancel();
        timer?.Dispose();
        _source.Dispose();
    }

    // --- Lecture ---

    private void OnTick() => _ = ReadOnceAsync(_stop.Token);

    /// <summary>
    /// Une lecture de la source, ignorée si la précédente n'est pas terminée, puis réarmement du minuteur. Elle est
    /// limitée à max(10 s, 3 × Interval). Ne lève jamais. Sur délai (ou arrêt), l'état est publié tout de suite mais la
    /// lecture abandonnée garde la place : elle la libère, et réarme le minuteur, quand elle se termine vraiment.
    /// </summary>
    private async Task ReadOnceAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0) return;
        var abandoned = false;
        try
        {
            var limit = ReadTimeout();
            using var timeout = new CancellationTokenSource(limit, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            Task<SourceReading>? pending = null;
            SourceReading reading;
            try
            {
                pending = _source.ReadAsync(linked.Token);
                // WaitAsync : une source qui ignore le jeton ne bloque pas le module au-delà du délai.
                reading = await pending.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (pending is { IsCompleted: false })
                {
                    // Lecture abandonnée mais encore en cours : pas de nouvelle lecture avant sa fin réelle. Son éventuelle
                    // exception est consommée pour qu'elle reste muette.
                    abandoned = true;
                    _ = pending.ContinueWith(
                        t =>
                        {
                            _ = t.Exception;
                            EndRead(ct);
                        },
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                {
                    _ = pending?.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }

                // Module arrêté (ou démarrage annulé) : la fin de la lecture n'est pas une erreur.
                if (ct.IsCancellationRequested) return;

                if (timeout.IsCancellationRequested)
                {
                    _log.Warning($"Lecture du débit réseau sans réponse après {limit.TotalSeconds:0} s.");
                }
                else
                {
                    _log.Error($"Lecture du débit réseau impossible : {ex.Message}");
                }
                Publish(null, new ModuleStatus(ModuleHealth.Offline));
                return;
            }

            Publish(reading.Sample, reading.Status);
        }
        finally
        {
            if (!abandoned) EndRead(ct);
        }
    }

    /// <summary>Libère la place de lecture, puis réarme le minuteur.</summary>
    private void EndRead(CancellationToken ct)
    {
        Volatile.Write(ref _reading, 0);
        ScheduleNext(ct);
    }

    /// <summary>Délai accordé à une lecture : 10 s au moins, ou trois fois l'intervalle de la source.</summary>
    private TimeSpan ReadTimeout()
    {
        var limit = _source.Interval * 3;
        return limit > MinReadTimeout ? limit : MinReadTimeout;
    }

    /// <summary>Arme le minuteur pour la prochaine lecture, sauf si le module est arrêté.</summary>
    private void ScheduleNext(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        ITimer? timer;
        lock (_gate)
        {
            if (_disposed) return;
            timer = _timer;
        }

        try
        {
            timer?.Change(_source.Interval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Le module a été libéré entre-temps : plus de lecture à programmer.
        }
    }

    /// <summary>
    /// Publie le résultat d'une lecture. Sans échantillon (lecture ratée ou sur délai comprises), les valeurs affichées
    /// sont effacées : une valeur périmée ne doit pas passer pour du direct. La courbe, elle, garde son historique.
    /// </summary>
    private void Publish(ThroughputSample? sample, ModuleStatus status)
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            if (sample is not null) _history.Add(_time.GetUtcNow(), sample);
            Volatile.Write(ref _latest, sample);

            changed = !Equals(_status, status);
            if (changed) Volatile.Write(ref _status, status);
        }

        if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    // --- Dessin ---

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var bounds = ctx.Bounds;
        if (bounds.Width <= 0f || bounds.Height <= 0f) return;

        var status = Status;
        var sample = Volatile.Read(ref _latest);
        var scale = ctx.TextScale;
        var theme = ctx.Theme;

        canvas.Save();
        canvas.ClipRect(bounds);
        try
        {
            if (ctx.Size.Rows < 2)
            {
                // En une rangée, ni titre ni courbe : le message d'action, l'état s'il n'est pas en direct, ou les valeurs.
                var area = SKRect.Inflate(bounds, -Padding * scale, -CompactPaddingY * scale);
                if (status.ActionMessage is { } message) ModuleDrawing.DrawActionMessage(canvas, area, theme, scale, message);
                else if (status.Health != ModuleHealth.Online) ModuleDrawing.DrawActionMessage(canvas, area, theme, scale, StateText(status));
                else DrawValues(canvas, area, sample, theme, scale);
                return;
            }

            var content = SKRect.Inflate(bounds, -Padding * scale, -Padding * scale);
            if (content.Width <= 0f || content.Height <= 0f) return;

            var titleHeight = TitleSize * scale;
            DrawTitle(canvas, content, titleHeight, status, theme, scale);

            var body = new SKRect(content.Left, content.Top + titleHeight + SectionGap * scale, content.Right, content.Bottom);
            if (body.Height <= 0f) return;

            if (status.ActionMessage is { } action)
            {
                ModuleDrawing.DrawActionMessage(canvas, body, theme, scale, action);
                return;
            }

            // La courbe prend 62 px au plus ; si la place manque elle cède aux valeurs, qui priment.
            var gap = SectionGap * scale;
            var curveHeight = Math.Min(CurveHeight * scale, body.Height - ValueSize * scale - gap);
            if (curveHeight < MinCurveHeight * scale) curveHeight = 0f;

            var valuesBottom = curveHeight > 0f ? body.Bottom - curveHeight - gap : body.Bottom;
            DrawValues(canvas, new SKRect(body.Left, body.Top, body.Right, valuesBottom), sample, theme, scale);
            if (curveHeight > 0f)
            {
                DrawCurve(canvas, new SKRect(body.Left, body.Bottom - curveHeight, body.Right, body.Bottom), theme, scale);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static string StateText(ModuleStatus status) => status.Health switch
    {
        ModuleHealth.Online => "en direct",
        ModuleHealth.NotConfigured => "à configurer",
        _ => "hors ligne",
    };

    private void DrawTitle(SKCanvas canvas, SKRect content, float height, ModuleStatus status, Theme theme, float scale)
    {
        var online = status.Health == ModuleHealth.Online;
        using var font = new SKFont(theme.Typeface, height);
        using var mutedPaint = new SKPaint { Color = theme.TextMuted, IsAntialias = true };
        using var statePaint = new SKPaint { Color = online ? theme.TextMuted : theme.Warning, IsAntialias = true };
        var mid = content.Top + height / 2f;

        var state = StateText(status);
        var stateWidth = Math.Min(ModuleDrawing.MeasureText(state, font), content.Width);
        DrawCentered(canvas, state, content.Right, mid, SKTextAlign.Right, font, statePaint, stateWidth);

        var titleWidth = content.Width - stateWidth - ColumnGap * scale;
        DrawCentered(canvas, _source.Title, content.Left, mid, SKTextAlign.Left, font, mutedPaint, titleWidth);
    }

    /// <summary>Débit descendant puis montant, côte à côte et centrés verticalement dans <paramref name="area"/>.</summary>
    private static void DrawValues(SKCanvas canvas, SKRect area, ThroughputSample? sample, Theme theme, float scale)
    {
        if (area.Width <= 0f || area.Height <= 0f) return;

        var down = sample is null ? (NoValue, string.Empty) : RateText.Format(sample.DownBytesPerSecond);
        var up = sample is null ? (NoValue, string.Empty) : RateText.Format(sample.UpBytesPerSecond);
        var columnWidth = (area.Width - ColumnGap * scale) / 2f;
        if (columnWidth <= 0f) return;

        using var valueFont = new SKFont(theme.Typeface, ValueSize * scale);
        using var unitFont = new SKFont(theme.Typeface, UnitSize * scale);

        // Même réduction pour les deux colonnes si l'une ne tient pas, en largeur comme en hauteur.
        var natural = Math.Max(NaturalWidth(down, valueFont, unitFont, scale), NaturalWidth(up, valueFont, unitFont, scale));
        var fit = Math.Min(1f, Math.Min(columnWidth / natural, area.Height / valueFont.Spacing));

        DrawBlock(canvas, down, area.Left, area.MidY, true, theme.Accent, theme, scale, fit);
        DrawBlock(canvas, up, area.Right - columnWidth, area.MidY, false, theme.TextMuted, theme, scale, fit);
    }

    private static float NaturalWidth((string Value, string Unit) rate, SKFont valueFont, SKFont unitFont, float scale)
    {
        var width = (ArrowWidth + ArrowGap) * scale + ModuleDrawing.MeasureText(rate.Value, valueFont);
        if (rate.Unit.Length > 0) width += UnitGap * scale + ModuleDrawing.MeasureText(rate.Unit, unitFont);
        return width;
    }

    /// <summary>Flèche pleine, valeur et unité alignées sur la même ligne de base, à partir de <paramref name="left"/>.</summary>
    private static void DrawBlock(
        SKCanvas canvas, (string Value, string Unit) rate, float left, float mid, bool isDown,
        SKColor arrowColor, Theme theme, float scale, float fit)
    {
        var size = scale * fit;
        using var valueFont = new SKFont(theme.Typeface, ValueSize * size);
        using var unitFont = new SKFont(theme.Typeface, UnitSize * size);
        using var textPaint = new SKPaint { Color = theme.Text, IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = theme.TextMuted, IsAntialias = true };

        var metrics = valueFont.Metrics;
        var baseline = mid - (metrics.Ascent + metrics.Descent) / 2f;
        var capHeight = metrics.CapHeight > 0f ? metrics.CapHeight : 0.7f * valueFont.Size;

        // Une flèche dessinée (triangle plein), centrée sur la hauteur des chiffres : le glyphe ↓ manque à la police.
        var arrowWidth = ArrowWidth * size;
        var arrowHeight = ArrowHeight * size;
        var arrowMid = baseline - capHeight / 2f;
        using var arrow = new SKPath();
        if (isDown)
        {
            arrow.MoveTo(left, arrowMid - arrowHeight / 2f);
            arrow.LineTo(left + arrowWidth, arrowMid - arrowHeight / 2f);
            arrow.LineTo(left + arrowWidth / 2f, arrowMid + arrowHeight / 2f);
        }
        else
        {
            arrow.MoveTo(left, arrowMid + arrowHeight / 2f);
            arrow.LineTo(left + arrowWidth, arrowMid + arrowHeight / 2f);
            arrow.LineTo(left + arrowWidth / 2f, arrowMid - arrowHeight / 2f);
        }
        arrow.Close();
        using var arrowPaint = new SKPaint { Color = arrowColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(arrow, arrowPaint);

        var x = left + arrowWidth + ArrowGap * size;
        var valueWidth = ModuleDrawing.MeasureText(rate.Value, valueFont);
        ModuleDrawing.DrawText(canvas, rate.Value, x, baseline, SKTextAlign.Left, valueFont, textPaint);
        if (rate.Unit.Length > 0)
        {
            ModuleDrawing.DrawText(canvas, rate.Unit, x + valueWidth + UnitGap * size, baseline, SKTextAlign.Left, unitFont, mutedPaint);
        }
    }

    /// <summary>
    /// Courbe des trois dernières minutes : l'échantillon le plus récent est à droite, l'échelle verticale est le
    /// maximum de la fenêtre avec un plancher de 1 Mb/s.
    /// </summary>
    private void DrawCurve(SKCanvas canvas, SKRect area, Theme theme, float scale)
    {
        var points = _history.Snapshot();
        if (points.Count < 2 || area.Width <= 0f || area.Height <= 0f) return;

        var scaleMax = MinScaleBytesPerSecond;
        foreach (var (_, s) in points) scaleMax = Math.Max(scaleMax, Math.Max(s.DownBytesPerSecond, s.UpBytesPerSecond));

        // La marge verticale garde le trait entier dans la zone, même sur les valeurs extrêmes.
        var inset = DownStroke * scale / 2f;
        var top = area.Top + inset;
        var height = area.Height - 2f * inset;
        var newest = points[^1].At;
        var oldest = newest - HistoryWindow;

        using var up = BuildPath(points, s => s.UpBytesPerSecond, area, top, height, oldest, scaleMax);
        using var down = BuildPath(points, s => s.DownBytesPerSecond, area, top, height, oldest, scaleMax);
        using var upPaint = new SKPaint
        {
            Color = theme.TextMuted, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = UpStroke * scale,
            StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
        };
        using var downPaint = new SKPaint
        {
            Color = theme.Accent, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = DownStroke * scale,
            StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawPath(up, upPaint);
        canvas.DrawPath(down, downPaint);
    }

    private static SKPath BuildPath(
        IReadOnlyList<(DateTimeOffset At, ThroughputSample Sample)> points, Func<ThroughputSample, double> value,
        SKRect area, float top, float height, DateTimeOffset oldest, double scaleMax)
    {
        var path = new SKPath();
        for (var i = 0; i < points.Count; i++)
        {
            var fractionX = (points[i].At - oldest).TotalSeconds / HistoryWindow.TotalSeconds;
            var x = area.Left + (float)Math.Clamp(fractionX, 0d, 1d) * area.Width;
            var fractionY = Math.Clamp(value(points[i].Sample) / scaleMax, 0d, 1d);
            var y = top + height - (float)fractionY * height;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        return path;
    }

    /// <summary>Dessine le texte (ellipsé à <paramref name="maxWidth"/>) centré verticalement sur <paramref name="mid"/>.</summary>
    private static void DrawCentered(SKCanvas canvas, string text, float x, float mid, SKTextAlign align, SKFont font, SKPaint paint, float maxWidth)
    {
        if (maxWidth <= 0f) return;
        var metrics = font.Metrics;
        var baseline = mid - (metrics.Ascent + metrics.Descent) / 2f;
        ModuleDrawing.DrawText(canvas, ModuleDrawing.Ellipsize(text, font, maxWidth), x, baseline, align, font, paint);
    }
}
