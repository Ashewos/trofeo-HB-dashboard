using System.Runtime.InteropServices;
using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public class JpegEncoderTests
{
    private static SKBitmap RedLeftBlueRight()
    {
        var frame = new SKBitmap(1920, 462);
        using var canvas = new SKCanvas(frame);
        canvas.DrawRect(0, 0, 960, 462, new SKPaint { Color = SKColors.Red });
        canvas.DrawRect(960, 0, 960, 462, new SKPaint { Color = SKColors.Blue });
        return frame;
    }

    [Fact]
    public void Encode_IsJpeg()
    {
        using var frame = new SKBitmap(1920, 462);
        frame.Erase(SKColors.Black);

        var bytes = JpegEncoder.Encode(frame, 0);

        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }

    [Fact]
    public void Encode_Rotation180()
    {
        using var frame = RedLeftBlueRight();

        using var decoded = SKBitmap.Decode(JpegEncoder.Encode(frame, 180));

        var left = decoded.GetPixel(200, 200);
        Assert.True(left.Blue > 200 && left.Red < 60, $"attendu du bleu à gauche, obtenu {left}");
        var right = decoded.GetPixel(1700, 200);
        Assert.True(right.Red > 200 && right.Blue < 60, $"attendu du rouge à droite, obtenu {right}");
    }

    [Fact]
    public void Encode_Rotation0_KeepsOrientation()
    {
        using var frame = RedLeftBlueRight();

        using var decoded = SKBitmap.Decode(JpegEncoder.Encode(frame, 0));

        var left = decoded.GetPixel(200, 200);
        Assert.True(left.Red > 200 && left.Blue < 60, $"attendu du rouge à gauche, obtenu {left}");
    }

    [Fact]
    public void Encode_InvalidRotation_Throws()
    {
        using var frame = new SKBitmap(16, 16);
        Assert.Throws<ArgumentOutOfRangeException>(() => JpegEncoder.Encode(frame, 90));
    }

    [Fact]
    public void Encode_TooLarge_LowersQuality()
    {
        using var frame = new SKBitmap(1920, 462);
        var pixels = new byte[1920 * 462 * 4];
        new Random(42).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        Marshal.Copy(pixels, 0, frame.GetPixels(), pixels.Length);

        var bytes = JpegEncoder.Encode(frame, 0);

        Assert.True(bytes.Length <= JpegEncoder.MaxBytes, $"taille {bytes.Length}");
    }
}
