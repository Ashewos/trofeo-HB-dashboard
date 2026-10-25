using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Clock;

/// <summary>
/// Heure en grand et date dessous, redessinées à chaque changement de minute, avec la météo du lieu choisi
/// (lue au démarrage puis toutes les 15 min) en haut à droite.
/// </summary>
public sealed class ClockModule : IModule
{
    private const float Padding = 22f;
    private const float TimeSize = 104f;
    private const float DateSize = 20f;
    private const float Gap = 6f;

    // Bloc météo (2 rangées ou plus), avant multiplication par l'échelle de texte.
    private const float BlockTopInset = 20f;
    private const float BlockIconWidth = 54f;
    private const float BlockIconGap = 4f;
    private const float BlockTemperatureSize = 46f;
    private const float BlockLabelSize = 16f;
    private const float BlockMaxLabelFraction = 0.45f;

    // Météo sur une rangée, à partir de 4 colonnes.
    private const int CompactMinColumns = 4;
    private const float CompactTemperatureSize = 30f;
    private const float CompactIconWidth = 30f;
    private const float CompactGap = 8f;

    /// <summary>Espace minimal entre l'heure et la météo.</summary>
    private const float WeatherClearance = 24f;

    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryPeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    /// <summary>Dernière lecture réussie et son instant ; immuable, échangé d'un bloc.</summary>
    private sealed record Reading(WeatherNow Weather, DateTimeOffset At);

    private readonly ClockSettings _settings;
    private readonly IWeatherProvider _weather;
    private readonly TimeProvider _time;
    private readonly IModuleLog _log;
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _gate = new();
    private ITimer? _minuteTimer;
    private ITimer? _weatherTimer;
    private bool _started;
    private bool _fetching;
    private bool _disposed;
    private Reading? _reading;
    private ModuleStatus _status = new(ModuleHealth.Online);

    public ClockModule(ModuleDescriptor descriptor, ClockSettings settings, IWeatherProvider weather, TimeProvider time, IModuleLog log)
    {
        Descriptor = descriptor;
        _settings = settings;
        _weather = weather;
        _time = time;
        _log = log;
    }

    public ModuleDescriptor Descriptor { get; }

    /// <summary>En ligne, sauf si le dernier essai de lecture de la météo a échoué (l'heure reste affichée).</summary>
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
            _minuteTimer = _time.CreateTimer(_ => OnTick(), null, UntilNextMinute(), Timeout.InfiniteTimeSpan);
            // Minuteur à un coup, réarmé après chaque essai : deux lectures ne se chevauchent jamais.
            if (_settings.Location is not null)
            {
                _weatherTimer = _time.CreateTimer(_ => _ = RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        if (_settings.Location is not null) _ = RefreshAsync();
        return Task.CompletedTask;
    }

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        var local = TimeZoneInfo.ConvertTime(ctx.Now, _time.LocalTimeZone).DateTime;
        var time = ClockText.Time(local, _settings.Format);
        var date = ClockText.Date(local);

        var scale = ctx.TextScale;
        WeatherText? weatherText = null;
        var icon = WeatherIcon.Unknown;
        if (CurrentWeather() is { } weather)
        {
            (icon, var label) = WeatherCodes.Describe(weather.WeatherCode, weather.IsDay);
            weatherText = new WeatherText(FormatTemperature(weather.Temperature), label);
        }

        var layout = Layout(ctx.Bounds, ctx.Size, ctx.Theme.Typeface, scale, time, date, weatherText);

        using var timeFont = new SKFont(ctx.Theme.Typeface, TimeSize * scale * layout.Fit);
        using var dateFont = new SKFont(ctx.Theme.Typeface, DateSize * scale * layout.Fit);
        using var timePaint = new SKPaint { Color = ctx.Theme.Text, IsAntialias = true };
        using var datePaint = new SKPaint { Color = ctx.Theme.TextMuted, IsAntialias = true };
        canvas.DrawText(time, layout.Time.Left, layout.TimeBaseline, SKTextAlign.Left, timeFont, timePaint);
        canvas.DrawText(date, layout.Date.Left, layout.DateBaseline, SKTextAlign.Left, dateFont, datePaint);

        if (layout.Weather.IsEmpty || weatherText is not { } text) return;

        WeatherIcons.Draw(canvas, icon, layout.Icon);
        using var temperatureFont = new SKFont(ctx.Theme.Typeface, layout.TemperatureSize);
        DrawInLine(canvas, text.Temperature, layout.TemperatureLine, temperatureFont, timePaint);
        if (layout.LabelLine.IsEmpty) return;
        using var labelFont = new SKFont(ctx.Theme.Typeface, BlockLabelSize * scale);
        DrawInLine(canvas, layout.Label, layout.LabelLine, labelFont, datePaint);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _minuteTimer?.Dispose();
            _weatherTimer?.Dispose();
            _minuteTimer = null;
            _weatherTimer = null;
        }

        // Hors du verrou : l'annulation exécute les rappels de la lecture en cours.
        _cancel.Cancel();
        _cancel.Dispose();
    }

    /// <summary>La météo à afficher : la dernière lecture réussie, si elle date de 1 h au plus et a une valeur valide.</summary>
    internal WeatherNow? CurrentWeather()
    {
        var reading = Volatile.Read(ref _reading);
        if (reading is null || _time.GetUtcNow() - reading.At > MaxAge) return null;

        var temperature = reading.Weather.Temperature;
        return double.IsFinite(temperature) && Math.Abs(temperature) < 1000 ? reading.Weather : null;
    }

    /// <summary>Température arrondie, sans lettre d'unité : <c>"12°"</c>.</summary>
    internal static string FormatTemperature(double value) =>
        ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "°";

    /// <summary>Textes du bloc météo.</summary>
    internal readonly record struct WeatherText(string Temperature, string Label);

    /// <summary>Position des éléments : l'heure et la date (rectangles de leur texte) et le bloc météo (vide si absent).</summary>
    internal readonly record struct ClockLayout(
        float Fit,
        SKRect Time,
        SKRect Date,
        float TimeBaseline,
        float DateBaseline,
        SKRect Weather,
        SKRect Icon,
        SKRect TemperatureLine,
        float TemperatureSize,
        SKRect LabelLine,
        string Label);

    /// <summary>
    /// Dispose l'heure, la date et la météo. Avec 2 rangées ou plus la météo est un bloc en haut à droite ; sur
    /// une rangée elle tient en « 12° » et une petite icône au bord droit, à partir de 4 colonnes. L'heure et
    /// la date se réduisent pour rester à 24 px au moins de la météo.
    /// </summary>
    internal static ClockLayout Layout(
        SKRect bounds, GridSize size, SKTypeface typeface, float scale, string time, string date, WeatherText? weather)
    {
        var availableWidth = bounds.Width - 2 * Padding;
        var availableHeight = bounds.Height - 2 * Padding;

        using var timeFont = new SKFont(typeface, TimeSize * scale);
        using var dateFont = new SKFont(typeface, DateSize * scale);
        var timeWidth = timeFont.MeasureText(time);
        var dateWidth = dateFont.MeasureText(date);
        var timeMetrics = timeFont.Metrics;
        var dateMetrics = dateFont.Metrics;

        // Les ascendants, descendants et largeurs sont proportionnels à la taille : on réduit par un seul facteur.
        ClockLayout Compose(float timeLimit, float dateLimit, ClockLayout block)
        {
            var fit = 1f;
            if (timeWidth > timeLimit) fit = Math.Min(fit, timeLimit / timeWidth);
            if (dateWidth > dateLimit) fit = Math.Min(fit, dateLimit / dateWidth);
            var blockHeight = (TimeSize + Gap + DateSize) * scale;
            if (blockHeight > availableHeight && availableHeight > 0) fit = Math.Min(fit, availableHeight / blockHeight);

            var left = bounds.Left + Padding;
            var dateBaseline = bounds.Bottom - Padding - dateMetrics.Descent * fit;
            var timeBaseline = dateBaseline + dateMetrics.Ascent * fit - Gap * scale * fit;
            return block with
            {
                Fit = fit,
                Time = new SKRect(left, timeBaseline + timeMetrics.Ascent * fit, left + timeWidth * fit, timeBaseline + timeMetrics.Descent * fit),
                Date = new SKRect(left, dateBaseline + dateMetrics.Ascent * fit, left + dateWidth * fit, dateBaseline + dateMetrics.Descent * fit),
                TimeBaseline = timeBaseline,
                DateBaseline = dateBaseline,
            };
        }

        var block = weather is { } text ? PlaceWeather(bounds, size, typeface, scale, text, availableWidth) : default;
        var textWidth = availableWidth - (block.Weather.Width + WeatherClearance);
        if (block.Weather.IsEmpty || textWidth <= 0f)
        {
            // Pas de météo, ou pas de place pour elle : l'heure garde toute la largeur.
            return Compose(availableWidth, availableWidth, default);
        }

        // L'heure et la date sont bornées à la place restante, avec un même facteur de réduction.
        return Compose(textWidth, textWidth, block);
    }

    private static ClockLayout PlaceWeather(
        SKRect bounds, GridSize size, SKTypeface typeface, float scale, WeatherText text, float availableWidth)
    {
        var right = bounds.Right - Padding;

        if (size.Rows >= 2)
        {
            var cell = WeatherIcons.CellSize(BlockIconWidth * scale, BlockIconWidth * scale * WeatherIcons.Rows / WeatherIcons.Columns);
            var iconWidth = cell * WeatherIcons.Columns;
            var iconHeight = cell * WeatherIcons.Rows;

            using var temperatureFont = new SKFont(typeface, BlockTemperatureSize * scale);
            using var labelFont = new SKFont(typeface, BlockLabelSize * scale);
            var label = ModuleDrawing.Ellipsize(text.Label, labelFont, Math.Max(iconWidth, availableWidth * BlockMaxLabelFraction));
            var temperatureWidth = ModuleDrawing.MeasureText(text.Temperature, temperatureFont);
            var labelWidth = ModuleDrawing.MeasureText(label, labelFont);
            var blockWidth = Math.Max(iconWidth, Math.Max(temperatureWidth, labelWidth));

            var top = bounds.Top + BlockTopInset;
            var icon = new SKRect(right - iconWidth, top, right, top + iconHeight);
            var temperatureTop = icon.Bottom + BlockIconGap * scale;
            var temperatureLine = new SKRect(right - temperatureWidth, temperatureTop, right, temperatureTop + BlockTemperatureSize * scale);
            var labelLine = new SKRect(right - labelWidth, temperatureLine.Bottom, right, temperatureLine.Bottom + labelFont.Spacing);
            var weather = new SKRect(right - blockWidth, top, right, labelLine.Bottom);
            return new ClockLayout(1f, default, default, 0f, 0f, weather, icon, temperatureLine, BlockTemperatureSize * scale, labelLine, label);
        }

        if (size.Columns >= CompactMinColumns)
        {
            var cell = WeatherIcons.CellSize(CompactIconWidth * scale, CompactIconWidth * scale * WeatherIcons.Rows / WeatherIcons.Columns);
            var iconWidth = cell * WeatherIcons.Columns;
            var iconHeight = cell * WeatherIcons.Rows;

            using var temperatureFont = new SKFont(typeface, CompactTemperatureSize * scale);
            var temperatureWidth = ModuleDrawing.MeasureText(text.Temperature, temperatureFont);

            var icon = new SKRect(right - iconWidth, bounds.MidY - iconHeight / 2f, right, bounds.MidY + iconHeight / 2f);
            var temperatureRight = icon.Left - CompactGap * scale;
            var lineHeight = CompactTemperatureSize * scale;
            var temperatureLine = new SKRect(temperatureRight - temperatureWidth, bounds.MidY - lineHeight / 2f, temperatureRight, bounds.MidY + lineHeight / 2f);
            var weather = new SKRect(temperatureLine.Left, Math.Min(icon.Top, temperatureLine.Top), right, Math.Max(icon.Bottom, temperatureLine.Bottom));
            return new ClockLayout(1f, default, default, 0f, 0f, weather, icon, temperatureLine, CompactTemperatureSize * scale, SKRect.Empty, string.Empty);
        }

        return default;
    }

    /// <summary>Dessine le texte aligné à droite de la ligne, centré verticalement dans celle-ci.</summary>
    private static void DrawInLine(SKCanvas canvas, string text, SKRect line, SKFont font, SKPaint paint)
    {
        var metrics = font.Metrics;
        var baseline = line.MidY - (metrics.Ascent + metrics.Descent) / 2f;
        ModuleDrawing.DrawText(canvas, text, line.Right, baseline, SKTextAlign.Right, font, paint);
    }

    /// <summary>Lit la météo, garde le résultat et arme le prochain essai : 15 min après un succès, 2 min après un échec.</summary>
    private async Task RefreshAsync()
    {
        var where = _settings.Location;
        if (where is null) return;

        CancellationToken ct;
        lock (_gate)
        {
            if (_disposed || _fetching) return;
            _fetching = true;
            ct = _cancel.Token;
        }

        WeatherNow? now = null;
        try
        {
            now = await _weather.GetCurrentAsync(where, _settings.Unit, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // le module est libéré : rien à rapporter
        }
        catch (WeatherException ex)
        {
            _log.Warning($"Météo indisponible : {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Error($"Météo : erreur inattendue : {ex}");
        }

        bool statusChanged;
        lock (_gate)
        {
            _fetching = false;
            if (_disposed) return;

            if (now is not null) Volatile.Write(ref _reading, new Reading(now, _time.GetUtcNow()));
            statusChanged = Transition(now is not null ? ModuleHealth.Online : ModuleHealth.Offline);
            _weatherTimer?.Change(now is not null ? RefreshPeriod : RetryPeriod, Timeout.InfiniteTimeSpan);
        }

        if (statusChanged) StatusChanged?.Invoke(this, EventArgs.Empty);
        if (now is not null || statusChanged) Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnTick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Recalcule l'échéance à chaque minute pour ne pas dériver.
            _minuteTimer?.Change(UntilNextMinute(), Timeout.InfiniteTimeSpan);
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Change l'état s'il diffère ; à appeler sous <see cref="_gate"/>. Renvoie vrai s'il a changé.</summary>
    private bool Transition(ModuleHealth health)
    {
        if (_status.Health == health) return false;
        Volatile.Write(ref _status, new ModuleStatus(health));
        return true;
    }

    private TimeSpan UntilNextMinute()
    {
        var sinceMinute = _time.GetLocalNow().TimeOfDay.Ticks % TimeSpan.TicksPerMinute;
        return TimeSpan.FromTicks(TimeSpan.TicksPerMinute - sinceMinute);
    }
}
