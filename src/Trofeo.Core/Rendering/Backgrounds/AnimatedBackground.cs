using SkiaSharp;
using Trofeo.Core.Layout;

namespace Trofeo.Core.Rendering;

/// <summary>
/// GIF ou WebP animé. Mémoire constante : un seul tampon de décodage (à la taille du fichier) et une seule image
/// recadrée 1920 × 462, réutilisés à chaque image ; jamais toutes les images du fichier.
/// Le fichier reste ouvert pendant la lecture. Un fichier d'une seule image est traité comme une image fixe.
/// </summary>
public sealed class AnimatedBackground : IBackground
{
    /// <summary>Une durée de moins de 20 ms (0 compris) vaut 100 ms, comme dans les navigateurs.</summary>
    private const int MinDurationMs = 20, DefaultDurationMs = 100;

    private SKCodec? _codec;
    private SKBitmap? _buffer;      // image décodée, à la taille du fichier
    private SKBitmap? _screen;      // image recadrée 1920 × 462
    private SKCanvas? _canvas;
    private SKImage _current;
    private readonly SKImageInfo _bufferInfo;
    private readonly SKCodecFrameInfo[] _frames = [];
    private readonly int[] _starts = [];        // début de chaque image, en ms
    private readonly int[] _durations = [];
    private readonly int _total;
    private int _decoded = -1;      // index de l'image présente dans le tampon
    private int _frame;             // index de l'image affichée
    private int _version = 1;
    private TimeSpan? _until;

    // Fichier d'une seule image : image fixe.
    private AnimatedBackground(SKImage still) => _current = still;

    private AnimatedBackground(SKCodec codec, SKBitmap buffer, SKBitmap screen, SKCodecFrameInfo[] frames)
    {
        _codec = codec;
        _buffer = buffer;
        _bufferInfo = buffer.Info;
        _screen = screen;
        _canvas = new SKCanvas(screen);
        _frames = frames;
        _starts = new int[frames.Length];
        _durations = new int[frames.Length];
        int t = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            _starts[i] = t;
            _durations[i] = frames[i].Duration < MinDurationMs ? DefaultDurationMs : frames[i].Duration;
            t += _durations[i];
        }
        _total = t;
        _current = null!; // posé par TryLoad avant de rendre l'instance
        _until = TimeSpan.FromMilliseconds(_durations[0]);
    }

    /// <summary>
    /// Surface maximale d'une image animée : 4096 × 4096 px (16 777 216). Le tampon de décodage reste en mémoire toute la
    /// lecture (4 octets par pixel, 64 Mo au plus) ; au-delà, le fichier est refusé comme illisible.
    /// </summary>
    internal const long MaxPixels = 4096L * 4096L;

    /// <summary>Rend null, avec un avertissement, si le fichier manque ou est illisible. Ne lève jamais.</summary>
    public static AnimatedBackground? TryLoad(string path, Action<string>? warn)
    {
        var codec = BackgroundSupport.OpenCodec(path, warn);
        if (codec is null) return null;

        AnimatedBackground? background = null;
        try
        {
            if (codec.FrameCount <= 1)
            {
                var still = BackgroundSupport.DecodeCover(codec, out _);
                codec.Dispose(); // une image fixe ne garde pas le fichier ouvert
                codec = null;
                if (still is not null) return new AnimatedBackground(still);
            }
            else if ((long)codec.Info.Width * codec.Info.Height <= MaxPixels)
            {
                var frames = codec.FrameInfo;
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                SKBitmap? buffer = null, screen = null;
                try
                {
                    buffer = new SKBitmap(info);
                    screen = new SKBitmap(BackgroundSupport.ScreenInfo);
                    background = new AnimatedBackground(codec, buffer, screen, frames);
                    codec = null; // l'instance en est propriétaire (avec le tampon et l'écran)
                    buffer = screen = null;
                }
                finally
                {
                    buffer?.Dispose();
                    screen?.Dispose();
                }

                if (background.ShowFrame(0)) return background;
            }
        }
        catch (Exception)
        {
            // SkiaSharp lève une Exception ordinaire quand l'allocation échoue : jamais de plantage pour un fond.
        }

        background?.Dispose();
        codec?.Dispose();
        warn?.Invoke(BackgroundSupport.Unreadable(path));
        return null;
    }

    public bool IsAnimated => _codec is not null;

    public SKImage Current => _current;

    public int Version => _version;

    public TimeSpan? UntilNextFrame => _until;

    /// <summary>
    /// Montre l'image du temps écoulé, modulo la durée totale. Un fichier plus rapide que l'ordonnanceur garde sa
    /// vitesse : les images intermédiaires sont décodées dans le tampon mais jamais recadrées ni affichées.
    /// </summary>
    public bool Advance(TimeSpan elapsed)
    {
        if (_codec is null) return false;

        int ms = (int)(Math.Max(0L, (long)elapsed.TotalMilliseconds) % _total);
        int index = Array.BinarySearch(_starts, ms);
        if (index < 0) index = ~index - 1;
        _until = TimeSpan.FromMilliseconds(_starts[index] + _durations[index] - ms);

        if (index == _frame) return false;
        if (!ShowFrame(index)) return false; // décodage raté : on garde l'image actuelle
        _frame = index;
        _version++;
        return true;
    }

    /// <summary>Décode l'image <paramref name="index"/> puis la recadre dans l'image d'écran. Faux en cas d'échec.</summary>
    private bool ShowFrame(int index)
    {
        if (!Decode(index)) return false;

        // Le tampon et l'image d'écran sont réutilisés (mêmes adresses de pixels). La nouvelle image Skia est construite
        // avant de libérer l'ancienne : en cas d'exception, Current reste valable.
        using (var source = SKImage.FromPixels(_buffer!.PeekPixels()))
        {
            _canvas!.Clear(SKColors.Black);
            BackgroundSupport.DrawCover(_canvas, source);
        }
        var next = SKImage.FromPixels(_screen!.PeekPixels());
        _current?.Dispose();
        _current = next;
        return true;
    }

    /// <summary>
    /// Met l'image <paramref name="index"/> dans le tampon. Une image qui dépend d'une autre (RequiredFrame) se décode
    /// par-dessus celle-ci ; si elle n'est pas dans le tampon, on la reconstruit d'abord, en remontant jusqu'à une image
    /// autonome. Un retour en arrière (bouclage) repart ainsi de l'image 0.
    /// </summary>
    private bool Decode(int index)
    {
        if (_decoded == index) return true;

        int required = _frames[index].RequiredFrame;
        if (required >= 0 && required != _decoded && !Decode(required)) return false;

        var options = required >= 0 ? new SKCodecOptions(index, required) : new SKCodecOptions(index);
        var result = _codec!.GetPixels(_bufferInfo, _buffer!.GetPixels(), options);
        if (result != SKCodecResult.Success)
        {
            _decoded = -1; // tampon peut-être à moitié écrit
            return false;
        }
        _decoded = index;
        return true;
    }

    /// <summary>Nombre de bitmaps vivants : le tampon de décodage et l'image recadrée. Pour les tests.</summary>
    internal int BitmapCount => (_buffer is null ? 0 : 1) + (_screen is null ? 0 : 1);

    public void Dispose()
    {
        _current?.Dispose();
        _current = null!;
        _canvas?.Dispose();
        _canvas = null;
        _buffer?.Dispose();
        _buffer = null;
        _screen?.Dispose();
        _screen = null;
        _codec?.Dispose();
        _codec = null;
    }
}
