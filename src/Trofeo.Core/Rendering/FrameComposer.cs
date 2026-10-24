using System.Diagnostics;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Rendering;

/// <summary>Compose l'image complète de l'écran à partir de la disposition et des modules.</summary>
public sealed class FrameComposer
{
    private const string ErrorText = "Erreur du module";

    /// <summary>Peint le fond, puis pour chaque placement son panneau et le rendu du module (1920 × 462).</summary>
    public SKBitmap Compose(
        DashboardLayout layout,
        IReadOnlyDictionary<Guid, IModule> modules,
        Theme theme,
        DateTimeOffset now)
    {
        var bitmap = new SKBitmap(GridGeometry.Width, GridGeometry.Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(theme.Background);

        foreach (var placement in layout.Placements)
        {
            var rect = ToRect(GridGeometry.ModuleRect(placement.Column, placement.Row, placement.Columns, placement.Rows));
            PaintPanel(canvas, rect, theme);

            if (!modules.TryGetValue(placement.InstanceId, out var module))
            {
                continue;
            }

            int saved = canvas.Save();
            try
            {
                canvas.ClipRect(rect);
                var ctx = new ModuleRenderContext(
                    rect, new GridSize(placement.Columns, placement.Rows), theme, placement.TextSize.Factor(), now);
                module.Render(canvas, ctx);
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    $"Échec du rendu du module {placement.TypeId} ({placement.InstanceId}) : {ex}");
                canvas.RestoreToCount(saved);
                PaintErrorPanel(canvas, rect, theme);
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        return bitmap;
    }

    private static SKRect ToRect(SKRectI r) => SKRect.Create(r.Left, r.Top, r.Width, r.Height);

    private static void PaintPanel(SKCanvas canvas, SKRect rect, Theme theme)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = theme.Panel.WithAlpha((byte)Math.Round(theme.PanelOpacity * 255f)),
        };
        canvas.DrawRoundRect(rect, theme.CornerRadius, theme.CornerRadius, paint);
    }

    /// <summary>Efface ce que le module a pu peindre avant d'échouer, puis affiche le message d'erreur.</summary>
    private static void PaintErrorPanel(SKCanvas canvas, SKRect rect, Theme theme)
    {
        canvas.Save();
        canvas.ClipRect(rect);
        canvas.Clear(theme.Background);
        PaintPanel(canvas, rect, theme);

        using var font = new SKFont(theme.Typeface, 24f);
        float maxWidth = rect.Width - 16f;
        float width = font.MeasureText(ErrorText);
        if (width > maxWidth && maxWidth > 0)
        {
            font.Size = 24f * maxWidth / width;
        }

        using var paint = new SKPaint { IsAntialias = true, Color = theme.Warning };
        float baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2f;
        canvas.DrawText(ErrorText, rect.MidX, baseline, SKTextAlign.Center, font, paint);
        canvas.Restore();
    }
}
