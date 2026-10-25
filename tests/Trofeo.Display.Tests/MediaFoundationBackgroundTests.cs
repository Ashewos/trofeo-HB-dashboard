using System.Runtime.InteropServices;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.Display.Video;
using Vortice.MediaFoundation;

namespace Trofeo.Display.Tests;

/// <summary>
/// Lit de vraies vidéos MP4 par Media Foundation. Chaque vidéo est fabriquée au début du test par l'encodeur H.264
/// de Windows (64 × 36, 10 images de 100 ms) : pas de fichier binaire dans le dépôt. Le codec est avec perte,
/// donc les couleurs se comparent avec une tolérance.
/// </summary>
public sealed class MediaFoundationBackgroundTests : IDisposable
{
    private const int FrameCount = 10, FrameMs = 100, VideoWidth = 64, VideoHeight = 36;

    // Une couleur pure par image, toutes bien séparées (B, V, R en 0 ou 255) pour résister à la compression.
    private static readonly SKColor[] Palette =
    [
        new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(255, 255, 0), new(255, 0, 255),
        new(0, 255, 255), new(255, 255, 255), new(255, 128, 0), new(0, 128, 255), new(128, 0, 255),
    ];

    private static readonly SKColor Black = new(0, 0, 0);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "trofeo-video-tests-" + Guid.NewGuid().ToString("N"));

    public MediaFoundationBackgroundTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Video(Func<int, int, int, SKColor>? pixel = null, int frameMs = FrameMs, bool withAudio = false, int width = VideoWidth, int height = VideoHeight)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".mp4");
        WriteVideo(path, pixel ?? ((frame, _, _) => Palette[frame]), frameMs, withAudio, width, height);
        return path;
    }

    /// <summary>Couleur au centre de l'écran (la vidéo 16:9 est recadrée en 1920 × 462 : le centre reste dans l'image).</summary>
    private static SKColor Center(IBackground background) => Pixel(background, GridGeometry.Width / 2, GridGeometry.Height / 2);

    private static SKColor Pixel(IBackground background, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(background.Current);
        return bitmap.GetPixel(x, y);
    }

    private static void AssertColor(SKColor expected, SKColor actual, string because = "")
    {
        const int tolerance = 70; // H.264 + conversions YUV : quelques dizaines de niveaux d'écart
        Assert.True(
            Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance &&
            Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"attendu {expected}, obtenu {actual} {because}");
    }

    private static MediaFoundationBackground Open(string path)
    {
        var warnings = new List<string>();
        var background = MediaFoundationBackground.TryOpen(path, warnings.Add);
        Assert.Empty(warnings);
        return Assert.IsType<MediaFoundationBackground>(background);
    }

    [Fact]
    public void Video_FollowsElapsedTime()
    {
        using var video = Open(Video());

        Assert.True(video.IsAnimated);
        Assert.Equal(GridGeometry.Width, video.Current.Width);
        Assert.Equal(GridGeometry.Height, video.Current.Height);
        AssertColor(Palette[0], Center(video), "(image 0 dès l'ouverture)");
        int version = video.Version;

        Assert.False(video.Advance(TimeSpan.FromMilliseconds(30))); // toujours l'image 0
        Assert.Equal(version, video.Version);
        Assert.InRange(video.UntilNextFrame!.Value.TotalMilliseconds, 1, FrameMs);

        Assert.True(video.Advance(TimeSpan.FromMilliseconds(250))); // image 2 : l'image 1 est lue mais jamais montrée
        AssertColor(Palette[2], Center(video), "(250 ms)");
        Assert.True(video.Version > version);

        Assert.True(video.Advance(TimeSpan.FromMilliseconds(950)));
        AssertColor(Palette[9], Center(video), "(950 ms)");
    }

    [Fact]
    public void Video_LoopsWithoutBlackFrame()
    {
        using var video = Open(Video());

        // Trois tours : à chaque pas (au milieu d'une image) l'image attendue est là, et jamais un écran noir,
        // y compris pendant la dernière image et juste après le retour au début.
        for (int step = 0; step < 30; step++)
        {
            video.Advance(TimeSpan.FromMilliseconds(step * FrameMs + FrameMs / 2));
            var color = Center(video);
            AssertColor(Palette[step % FrameCount], color, $"(pas {step})");
            Assert.NotEqual(Black, color);
        }

        // Au début du tour suivant (après les 83 ms mini entre deux recadrages) : l'image 0 revient aussitôt.
        video.Advance(TimeSpan.FromMilliseconds(3 * FrameCount * FrameMs + 60));
        AssertColor(Palette[0], Center(video), "(retour à l'image 0)");
    }

    [Fact]
    public void Video_LongJump_StaysInSync()
    {
        using var video = Open(Video());

        // Plusieurs tours d'un coup (veille de l'ordinateur, écran éteint) : on retombe sur la bonne image.
        Assert.True(video.Advance(TimeSpan.FromMilliseconds(50 * FrameCount * FrameMs + 350)));
        AssertColor(Palette[3], Center(video));
    }

    [Fact]
    public void Video_IsNotFlipped()
    {
        // Haut rouge, bas bleu : une vidéo lue à l'envers (images « bottom-up » de Media Foundation) se verrait ici.
        using var video = Open(Video((_, _, y) => y < VideoHeight / 2 ? new SKColor(255, 0, 0) : new SKColor(0, 0, 255)));

        AssertColor(new SKColor(255, 0, 0), Pixel(video, 960, 40));
        AssertColor(new SKColor(0, 0, 255), Pixel(video, 960, 420));
    }

    [Fact]
    public void Video_PaddingRowsOfTheDecoder_AreNotShown()
    {
        // Le décodeur arrondit la hauteur à un multiple de 16 (116 → 128) : les lignes en trop ne doivent pas se voir.
        // Cette vidéo a presque le format de l'écran, donc son bord bas est visible à l'écran.
        using var video = Open(Video((_, _, _) => new SKColor(255, 0, 0), width: 480, height: 116));

        AssertColor(new SKColor(255, 0, 0), Pixel(video, 960, GridGeometry.Height - 3), "(bord bas)");
        AssertColor(new SKColor(255, 0, 0), Pixel(video, 960, 2), "(bord haut)");
    }

    [Fact]
    public void Video_RecropsAtMostTwelveTimesPerSecond()
    {
        // Vidéo à 50 images/s (20 ms par image), lue toutes les 10 ms pendant une seconde : l'image change, mais au plus 12 fois.
        using var video = Open(Video(frameMs: 20));

        int changes = 0;
        for (int ms = 10; ms <= 1000; ms += 10)
        {
            if (video.Advance(TimeSpan.FromMilliseconds(ms))) changes++;
            Assert.InRange(video.UntilNextFrame!.Value.TotalMilliseconds, 1, 1000);
        }

        Assert.InRange(changes, 8, 12);
    }

    [Fact]
    public void Video_WithAudioTrack_ReadsOnlyTheVideoStream()
    {
        // Pas de son, jamais : même si le fichier a une piste son, seul le flux vidéo est sélectionné.
        using var video = Open(Video(withAudio: true));

        Assert.Equal(2, video.StreamCount); // la piste son est bien dans le fichier…
        Assert.Equal(1, video.SelectedStreamCount); // …et n'est pas lue
        Assert.True(video.Advance(TimeSpan.FromMilliseconds(350)));
        AssertColor(Palette[3], Center(video));
    }

    [Fact]
    public void Video_BadFile_IsNull()
    {
        var path = Path.Combine(_folder, "abime.mp4");
        File.WriteAllBytes(path, Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 % 251)).ToArray());
        var warnings = new List<string>();

        var video = MediaFoundationBackground.TryOpen(path, warnings.Add);

        Assert.Null(video);
        Assert.Equal([$"Fond d'écran illisible : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Video_MissingFile_IsNull()
    {
        var path = Path.Combine(_folder, "absent.mp4");
        var warnings = new List<string>();

        Assert.Null(MediaFoundationBackground.TryOpen(path, warnings.Add));

        Assert.Equal([$"Fond d'écran introuvable : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Video_AudioOnlyFile_IsNull()
    {
        // Un fichier sans flux vidéo n'est pas un fond : illisible, et aucun son.
        var path = Path.Combine(_folder, "son.wav");
        File.WriteAllBytes(path, WavSilence());
        var warnings = new List<string>();

        Assert.Null(MediaFoundationBackground.TryOpen(path, warnings.Add));

        Assert.Equal([$"Fond d'écran illisible : {path}. Fond du thème utilisé."], warnings);
    }

    [Fact]
    public void Video_Dispose_IsIdempotent()
    {
        var video = Open(Video());
        video.Dispose();
        video.Dispose();
        Assert.False(video.Advance(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Video_StaysOpen_WhileFileIsReplacedOrDeleted()
    {
        // Comme les GIF : le fichier peut être remplacé ou supprimé pendant la lecture (sous Windows).
        var path = Video();
        using var video = Open(path);

        File.Delete(path);

        Assert.True(video.Advance(TimeSpan.FromMilliseconds(450)));
        AssertColor(Palette[4], Center(video));
    }

    private static byte[] WavSilence()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        const int dataLength = 8000;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(8000);
        writer.Write(8000);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(Enumerable.Repeat((byte)128, dataLength).ToArray());
        writer.Flush();
        return memory.ToArray();
    }

    /// <summary>
    /// Encode un MP4 H.264 de <see cref="FrameCount"/> images de <paramref name="frameMs"/> ms ; avec <paramref name="withAudio"/>,
    /// une piste son AAC (du silence) accompagne les images.
    /// </summary>
    private static void WriteVideo(string path, Func<int, int, int, SKColor> pixel, int frameMs, bool withAudio = false, int width = VideoWidth, int height = VideoHeight)
    {
        Assert.True(MediaFoundationBackground.EnsureStarted());

        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4);
        using var writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attributes);

        long frameDuration = frameMs * 10_000L; // unités de 100 ns
        using (var output = MediaFactory.MFCreateMediaType())
        {
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            output.Set(MediaTypeAttributeKeys.AvgBitrate, 400_000u);
            output.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            output.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height));
            output.Set(MediaTypeAttributeKeys.FrameRate, Pack(1000 / frameMs, 1));
            output.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            writer.AddStream(output);
        }

        using (var input = MediaFactory.MFCreateMediaType())
        {
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            input.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            input.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height));
            input.Set(MediaTypeAttributeKeys.FrameRate, Pack(1000 / frameMs, 1));
            input.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(width * 4)); // positif : de haut en bas
            writer.SetInputMediaType(0, input, null);
        }

        const int SampleRate = 44100, AudioBytesPerSecond = SampleRate * 4; // PCM 16 bits stéréo
        if (withAudio)
        {
            using (var output = MediaFactory.MFCreateMediaType())
            {
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                output.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                output.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
                output.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
                output.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 16000u);
                output.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 1u);
                output.Set(MediaTypeAttributeKeys.AacPayloadType, 0u);
                writer.AddStream(output);
            }

            using var input = MediaFactory.MFCreateMediaType();
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            input.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            input.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            input.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
            input.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
            input.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)AudioBytesPerSecond);
            input.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
            writer.SetInputMediaType(1, input, null);
        }

        writer.BeginWriting();
        var bytes = new byte[width * height * 4];
        var silence = new byte[AudioBytesPerSecond * frameMs / 1000];
        for (int frame = 0; frame < FrameCount; frame++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var color = pixel(frame, x, y);
                    int i = (y * width + x) * 4;
                    bytes[i] = color.Blue;
                    bytes[i + 1] = color.Green;
                    bytes[i + 2] = color.Red;
                    bytes[i + 3] = 255;
                }
            }

            WriteSample(writer, 0, bytes, frame * frameDuration, frameDuration);
            if (withAudio) WriteSample(writer, 1, silence, frame * frameDuration, frameDuration);
        }

        writer.Finalize();
    }

    private static void WriteSample(IMFSinkWriter writer, int stream, byte[] bytes, long time, long duration)
    {
        using var buffer = MediaFactory.MFCreateMemoryBuffer(bytes.Length);
        buffer.Lock(out var data, out _, out _);
        try { Marshal.Copy(bytes, 0, data, bytes.Length); }
        finally { buffer.Unlock(); }
        buffer.CurrentLength = bytes.Length;

        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = duration;
        writer.WriteSample(stream, sample);
    }

    private static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;
}
