using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Trofeo.Core.Layout;

namespace Trofeo.Settings.Controls;

/// <summary>
/// La bande en direct : la dernière image de l'écran, tournée de <see cref="Rotation"/> (180 : retournement horizontal et
/// vertical, comme la dalle est montée), mise à l'échelle sans déformation dans un cadre de rayon 10 en <c>Line</c>.
/// Sans image, elle dit « En attente de l'écran… » en <c>Muted</c>.
/// <para>
/// Le service envoie le JPEG tel que l'écran le reçoit, déjà tourné ; la bande le tourne de nouveau, donc elle montre
/// toujours le tableau de bord à l'endroit : le repère « écran » de <see cref="ScreenToStrip(Point)"/> est celui du damier
/// (<see cref="GridGeometry"/>, 1920 × 462 pixels, non tourné), quelle que soit la rotation.
/// </para>
/// </summary>
public sealed class ScreenStrip : Decorator
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ScreenStrip, IImage?>(nameof(Source));

    public static readonly StyledProperty<int> RotationProperty =
        AvaloniaProperty.Register<ScreenStrip, int>(nameof(Rotation));

    public static readonly StyledProperty<IBrush?> FrameBrushProperty =
        AvaloniaProperty.Register<ScreenStrip, IBrush?>(nameof(FrameBrush));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<ScreenStrip, CornerRadius>(nameof(CornerRadius), new CornerRadius(10));

    private const double FrameThickness = 1;
    private readonly TextBlock _waiting = new() { Text = Texts.StripWaiting };

    static ScreenStrip()
    {
        AffectsRender<ScreenStrip>(SourceProperty, RotationProperty, FrameBrushProperty, CornerRadiusProperty);
        AffectsMeasure<ScreenStrip>(SourceProperty);
        AffectsArrange<ScreenStrip>(SourceProperty);
        SourceProperty.Changed.AddClassHandler<ScreenStrip>((strip, _) => strip._waiting.IsVisible = strip.Source is null);
    }

    public ScreenStrip()
    {
        Child = _waiting;
        Focusable = false;
    }

    /// <summary>La dernière image de l'écran (JPEG décodé) ; <c>null</c> : on attend.</summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Rotation que l'écran applique à l'image, en degrés : 0 ou 180.</summary>
    public int Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    /// <summary>Rayon du cadre (10).</summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Le rectangle de la bande (en coordonnées du contrôle) où l'écran est montré : le plus grand 1920 × 462 qui tient
    /// dans le cadre, centré. Il existe aussi sans image.
    /// </summary>
    public Rect ContentRect
    {
        get
        {
            var inner = new Rect(Bounds.Size).Deflate(FrameThickness);
            if (inner.Width <= 0 || inner.Height <= 0)
                return default;
            var scale = Math.Min(inner.Width / GridGeometry.Width, inner.Height / GridGeometry.Height);
            var size = new Size(GridGeometry.Width * scale, GridGeometry.Height * scale);
            return new Rect(inner.X + (inner.Width - size.Width) / 2, inner.Y + (inner.Height - size.Height) / 2, size.Width, size.Height);
        }
    }

    /// <summary>Un point de l'écran (pixels 1920 × 462, non tourné) vers la bande (coordonnées du contrôle).</summary>
    public Point ScreenToStrip(Point screen)
    {
        var content = ContentRect;
        var scale = content.Width / GridGeometry.Width;
        return new Point(content.X + screen.X * scale, content.Y + screen.Y * scale);
    }

    /// <summary>Un rectangle de l'écran (pixels) vers la bande.</summary>
    public Rect ScreenToStrip(Rect screen) => new(ScreenToStrip(screen.TopLeft), ScreenToStrip(screen.BottomRight));

    /// <summary>Un point de la bande (coordonnées du contrôle) vers l'écran (pixels 1920 × 462). Peut sortir de l'écran.</summary>
    public Point StripToScreen(Point strip)
    {
        var content = ContentRect;
        var scale = content.Width > 0 ? content.Width / GridGeometry.Width : 1;
        return new Point((strip.X - content.X) / scale, (strip.Y - content.Y) / scale);
    }

    /// <summary>Un rectangle de la bande vers l'écran (pixels).</summary>
    public Rect StripToScreen(Rect strip) => new(StripToScreen(strip.TopLeft), StripToScreen(strip.BottomRight));

    protected override Size MeasureOverride(Size availableSize)
    {
        // La bande prend toute la largeur offerte et la hauteur qui garde le rapport 1920 × 462.
        var width = double.IsInfinity(availableSize.Width) ? GridGeometry.Width / 2.0 : availableSize.Width;
        var height = (width - 2 * FrameThickness) * GridGeometry.Height / GridGeometry.Width + 2 * FrameThickness;
        if (!double.IsInfinity(availableSize.Height) && height > availableSize.Height)
            height = availableSize.Height;
        _waiting.Measure(new Size(Math.Max(0, width - 2 * FrameThickness), Math.Max(0, height - 2 * FrameThickness)));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = ContentRect;
        _waiting.Arrange(content.Width > 0 ? content : new Rect(finalSize));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var content = ContentRect;
        if (content.Width <= 0)
            return;

        var radius = CornerRadius.TopLeft;
        var frame = content.Inflate(FrameThickness / 2);

        if (Source is { } image)
        {
            var inner = Math.Max(0, radius - FrameThickness);
            using (context.PushClip(new RoundedRect(content, inner)))
            using (context.PushTransform(Transform(content)))
            {
                context.DrawImage(image, new Rect(image.Size), content);
            }
        }

        if (FrameBrush is { } brush)
            context.DrawRectangle(null, new Pen(brush, FrameThickness), frame, radius, radius);
    }

    /// <summary>180° : retournement horizontal et vertical autour du centre de l'image.</summary>
    private Matrix Transform(Rect content)
    {
        if (Rotation % 360 != 180)
            return Matrix.Identity;
        var center = content.Center;
        return Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(-1, -1) * Matrix.CreateTranslation(center.X, center.Y);
    }
}
