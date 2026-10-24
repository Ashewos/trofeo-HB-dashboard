using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Clock;

/// <summary>Heure en grand et date dessous, redessinées à chaque changement de minute.</summary>
public sealed class ClockModule : IModule
{
    private const float Padding = 22f;
    private const float TimeSize = 104f;
    private const float DateSize = 20f;
    private const float Gap = 6f;

    private readonly ClockSettings _settings;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public ClockModule(ModuleDescriptor descriptor, ClockSettings settings, TimeProvider time)
    {
        Descriptor = descriptor;
        _settings = settings;
        _time = time;
    }

    public ModuleDescriptor Descriptor { get; }
    public ModuleStatus Status { get; } = new(ModuleHealth.Online);
    public bool IsAnimating => false;

    public event EventHandler? Invalidated;

    public Task StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) return Task.CompletedTask;
            _timer = _time.CreateTimer(_ => OnTick(), null, UntilNextMinute(), Timeout.InfiniteTimeSpan);
        }

        return Task.CompletedTask;
    }

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var local = TimeZoneInfo.ConvertTime(ctx.Now, _time.LocalTimeZone).DateTime;
        var time = ClockText.Time(local, _settings.Format);
        var date = ClockText.Date(local);

        var scale = ctx.TextScale;
        var availableWidth = ctx.Bounds.Width - 2 * Padding;
        var availableHeight = ctx.Bounds.Height - 2 * Padding;

        using var timeFont = new SKFont(ctx.Theme.Typeface, TimeSize * scale);
        using var dateFont = new SKFont(ctx.Theme.Typeface, DateSize * scale);

        // Réduit l'ensemble si le texte déborde en largeur ou en hauteur.
        var timeWidth = timeFont.MeasureText(time);
        var dateWidth = dateFont.MeasureText(date);
        var fit = 1f;
        if (timeWidth > availableWidth) fit = Math.Min(fit, availableWidth / timeWidth);
        if (dateWidth > availableWidth) fit = Math.Min(fit, availableWidth / dateWidth);
        var blockHeight = (TimeSize + Gap + DateSize) * scale;
        if (blockHeight > availableHeight && availableHeight > 0) fit = Math.Min(fit, availableHeight / blockHeight);
        if (fit < 1f)
        {
            timeFont.Size *= fit;
            dateFont.Size *= fit;
        }

        var dateMetrics = dateFont.Metrics;
        var x = ctx.Bounds.Left + Padding;
        var dateBaseline = ctx.Bounds.Bottom - Padding - dateMetrics.Descent;
        var timeBaseline = dateBaseline + dateMetrics.Ascent - Gap * scale * fit;

        using var timePaint = new SKPaint { Color = ctx.Theme.Text, IsAntialias = true };
        using var datePaint = new SKPaint { Color = ctx.Theme.TextMuted, IsAntialias = true };
        canvas.DrawText(time, x, timeBaseline, SKTextAlign.Left, timeFont, timePaint);
        canvas.DrawText(date, x, dateBaseline, SKTextAlign.Left, dateFont, datePaint);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnTick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Recalcule l'échéance à chaque minute pour ne pas dériver.
            _timer?.Change(UntilNextMinute(), Timeout.InfiniteTimeSpan);
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan UntilNextMinute()
    {
        var sinceMinute = _time.GetLocalNow().TimeOfDay.Ticks % TimeSpan.TicksPerMinute;
        return TimeSpan.FromTicks(TimeSpan.TicksPerMinute - sinceMinute);
    }
}
