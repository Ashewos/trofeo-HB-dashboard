using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Trofeo.Settings.Controls;

namespace Trofeo.Settings.Tests;

public class ScreenStripTests
{
    /// <summary>1920 × 462, bleu partout, avec un carré rouge de 160 px en haut à gauche.</summary>
    private static WriteableBitmap RedTopLeft()
    {
        var bitmap = new WriteableBitmap(new PixelSize(1920, 462), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        var row = new byte[buffer.RowBytes];
        for (var y = 0; y < 462; y++)
        {
            for (var x = 0; x < 1920; x++)
            {
                var red = x < 160 && y < 160;
                row[x * 4 + 0] = (byte)(red ? 0 : 255);   // B
                row[x * 4 + 1] = 0;                        // G
                row[x * 4 + 2] = (byte)(red ? 255 : 0);   // R
                row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, buffer.RowBytes);
        }
        return bitmap;
    }

    private static (byte R, byte G, byte B) Pixel(WriteableBitmap frame, int x, int y)
    {
        using var buffer = frame.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        // La capture est en Rgba8888 ou en Bgra8888 selon la plate-forme.
        return buffer.Format == PixelFormat.Bgra8888 ? (bytes[2], bytes[1], bytes[0]) : (bytes[0], bytes[1], bytes[2]);
    }

    private static (Window Window, ScreenStrip Strip) Show(int rotation)
    {
        var strip = new ScreenStrip { Source = RedTopLeft(), Rotation = rotation, Width = 960, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 1000, Height = 400, Content = strip };
        window.Show();
        return (window, strip);
    }

    [AvaloniaFact]
    public void ScreenStrip_Rotation180_FlipsImage()
    {
        var (window, strip) = Show(180);
        var frame = window.CaptureRenderedFrame()!;
        var right = (int)strip.Bounds.Right - 30;
        var bottom = (int)strip.Bounds.Bottom - 30;

        // Le carré rouge du coin haut-gauche de l'image est affiché en bas à droite.
        var (r, _, b) = Pixel(frame, right, bottom);
        Assert.True(r > 200 && b < 60, $"bas-droite attendu rouge, obtenu R={r} B={b}");
        var (r2, _, b2) = Pixel(frame, (int)strip.Bounds.Left + 30, (int)strip.Bounds.Top + 30);
        Assert.True(b2 > 200 && r2 < 60, $"haut-gauche attendu bleu, obtenu R={r2} B={b2}");
        window.Close();
    }

    [AvaloniaFact]
    public void ScreenStrip_Rotation0_KeepsImage()
    {
        var (window, strip) = Show(0);
        var frame = window.CaptureRenderedFrame()!;

        var (r, _, b) = Pixel(frame, (int)strip.Bounds.Left + 30, (int)strip.Bounds.Top + 30);
        Assert.True(r > 200 && b < 60, $"haut-gauche attendu rouge, obtenu R={r} B={b}");
        window.Close();
    }

    [AvaloniaFact]
    public void ScreenStrip_KeepsTheAspectRatio()
    {
        var (window, strip) = Show(180);

        Assert.Equal(960, strip.Bounds.Width, 1);
        Assert.Equal(960 * 462.0 / 1920 + 2, strip.Bounds.Height, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void ScreenStrip_MapsScreenPixelsToTheStripAndBack()
    {
        var (window, strip) = Show(180);
        var content = strip.ContentRect;

        Assert.Equal(content.TopLeft, strip.ScreenToStrip(new Point(0, 0)));
        var corner = strip.ScreenToStrip(new Point(1920, 462));
        Assert.Equal(content.Right, corner.X, 3);
        Assert.Equal(content.Bottom, corner.Y, 3);

        var back = strip.StripToScreen(strip.ScreenToStrip(new Point(700, 123)));
        Assert.Equal(700, back.X, 3);
        Assert.Equal(123, back.Y, 3);

        // Même sans image, le repère existe (la tâche 10 y pose le damier).
        strip.Source = null;
        Assert.Equal(content.TopLeft, strip.ScreenToStrip(new Point(0, 0)));
        window.Close();
    }
}
