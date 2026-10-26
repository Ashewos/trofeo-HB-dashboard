using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Vortice.MediaFoundation;

namespace Trofeo.Display.Video;

/// <summary>
/// Fond vidéo lu par Media Foundation (MP4/H.264, WMV… tout ce que Windows décode). Mémoire constante : un seul échantillon
/// décodé (celui qui est à l'écran, gardé tel quel, sans copie) et une seule image recadrée 1920 × 462, réutilisée.
/// Le décodeur lit toutes les images de la vidéo, l'une après l'autre : une vidéo à 60 images/s coûte plus qu'une à 24.
/// Mais on ne recadre (l'étape qui coûte des pixels) qu'au plus <see cref="MaxCropsPerSecond"/> fois par seconde.
/// Jamais de son : seul le flux vidéo est sélectionné. Au bout de la vidéo, retour au début sans image noire.
/// </summary>
public sealed class MediaFoundationBackground : IBackground
{
    /// <summary>Cadence maximale du recadrage, donc de l'image montrée et du flou du verre qui en dépend.</summary>
    internal const int MaxCropsPerSecond = 12;

    private static readonly TimeSpan CropInterval = TimeSpan.FromSeconds(1.0 / MaxCropsPerSecond);

    /// <summary>Durée d'une image quand le fichier ne la donne pas (ni durée d'échantillon, ni cadence).</summary>
    private static readonly TimeSpan FallbackDuration = TimeSpan.FromMilliseconds(33);

    /// <summary>Échantillons sans image (ticks, trous) lus de suite avant d'abandonner : une vidéo ne doit pas faire boucler l'affichage.</summary>
    private const int MaxEmptyReads = 200;

    /// <summary>
    /// Retard toléré entre la position voulue et la position lue. Au-delà (écran débranché, veille, machine saturée), la lecture
    /// ne rattrape pas : elle reprend là où elle en était, comme après une pause. Sans cela, le retour de l'écran décode toutes
    /// les images manquantes d'un coup, sur le thread d'affichage.
    /// </summary>
    private static readonly TimeSpan MaxLag = TimeSpan.FromSeconds(1);

    private static readonly Lazy<bool> Started = new(StartMediaFoundation);

    private readonly string _path;
    private Action<string>? _warn;      // posé une fois l'ouverture réussie : TryOpen avertit lui-même d'un échec à l'ouverture
    private IMFSourceReader? _reader;
    private IMFSample? _sample;         // l'image décodée à l'écran, gardée pour le recadrage
    private SKBitmap? _screen;          // image recadrée 1920 × 462
    private SKCanvas? _canvas;
    private SKImage _current;
    private int _width, _height, _stride;   // format de sortie (RGB32) ; une foulée négative : image de bas en haut
    private SKRectI _area;              // partie visible du format : les décodeurs arrondissent la taille (1088 lignes pour 1080)
    private TimeSpan _defaultDuration = FallbackDuration;
    private TimeSpan? _origin;          // horodatage de la première image : le début de la vidéo
    private TimeSpan _frameEnd;         // fin de l'image à l'écran, depuis le début du tour
    private TimeSpan _loopLength;       // durée d'un tour, connue une fois la fin atteinte
    private TimeSpan _offset;           // temps écoulé auquel le tour en cours a commencé
    private TimeSpan _lastCrop;         // temps écoulé du dernier recadrage
    private bool _ended;                // fin du fichier atteinte pendant ce tour
    private bool _fresh;                // un nouvel échantillon attend son recadrage
    private bool _broken;               // lecture impossible : on garde la dernière image
    private bool _still;                // vidéo d'une seule image : une image fixe, le lecteur est libéré
    private string? _tuningFailure;      // la cause du refus des réglages du décodeur (il garde alors son défaut)
    private string? _failure;           // la cause de la première panne, pour l'avertissement d'ouverture
    private int _tourSamples;           // échantillons lus pendant le tour en cours
    private int _version = 1;
    private TimeSpan? _until;

    private MediaFoundationBackground(string path, IMFSourceReader reader, SKBitmap screen)
    {
        _path = path;
        _reader = reader;
        _screen = screen;
        _canvas = new SKCanvas(screen);
        _current = null!; // posé par TryOpen avant de rendre l'instance
    }

    /// <summary>Rend null, avec un avertissement, si le fichier manque, n'a pas de flux vidéo ou n'est pas décodable. Ne lève jamais.</summary>
    public static MediaFoundationBackground? TryOpen(string path, Action<string>? warn) => TryOpen(path, warn, DecoderTuning.Default);

    internal static MediaFoundationBackground? TryOpen(string path, Action<string>? warn, DecoderTuning tuning)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            warn?.Invoke(BackgroundSupport.Missing(path));
            return null;
        }

        MediaFoundationBackground? background = null;
        var cause = "Media Foundation n'a pas pu démarrer";
        try
        {
            if (EnsureStarted())
            {
                background = Open(path, tuning);
                if (background.ReadFirstFrame())
                {
                    background._warn = warn;
                    if (background._tuningFailure is { } refused)
                    {
                        warn?.Invoke($"Réglage du décodeur vidéo refusé pour {path} ({refused}). Le décodeur garde ses réglages par défaut : la lecture marche, mais elle consomme plus.");
                    }
                    return background;
                }

                cause = background._failure ?? "aucune image décodable";
            }
        }
        catch (Exception ex)
        {
            // Codec absent, fichier abîmé, pas de flux vidéo : l'interop lève une exception COM. Jamais de plantage pour un fond.
            cause = Describe(ex);
        }

        background?.Dispose();
        warn?.Invoke(BackgroundSupport.Unreadable(path, cause));
        return null;
    }

    /// <summary>La cause d'une exception, pour le journal : son message et son HRESULT (le message est parfois vide ou obscur).</summary>
    private static string Describe(Exception ex)
    {
        var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message.Trim();
        return $"{message} [HRESULT 0x{ex.HResult:X8}]";
    }

    /// <summary>
    /// Démarre Media Foundation une fois pour le processus (version « lite » : sans réseau) ; on ne l'arrête jamais,
    /// elle ne tient que quelques ressources et sert tant que le service tourne. Vrai si elle est prête.
    /// </summary>
    internal static bool EnsureStarted() => Started.Value;

    private static bool StartMediaFoundation()
    {
        try
        {
            MediaFactory.MFStartup(true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static MediaFoundationBackground Open(string path, DecoderTuning tuning)
    {
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u); // convertit tout format décodé en RGB32
        var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        SKBitmap? screen = null;
        try
        {
            // Pas de son, jamais : on désélectionne tout, puis on ne garde que le premier flux vidéo.
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
            using (var wanted = MediaFactory.MFCreateMediaType())
            {
                wanted.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                wanted.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, wanted);
            }

            var tuningFailure = DecoderTuner.Apply(reader, tuning); // le décodeur existe depuis le type de sortie

            screen = new SKBitmap(BackgroundSupport.ScreenInfo);
            var background = new MediaFoundationBackground(path, reader, screen) { _tuningFailure = tuningFailure };
            reader = null;
            screen = null; // l'instance en est propriétaire
            return background;
        }
        finally
        {
            screen?.Dispose();
            reader?.Dispose();
        }
    }

    public bool IsAnimated => _reader is not null && !_broken;

    public SKImage Current => _current;

    public int Version => _version;

    public TimeSpan? UntilNextFrame => IsAnimated ? _until : null;

    /// <summary>
    /// Montre l'image du temps écoulé : le décodeur lit les images une à une jusqu'à celle qui couvre ce temps (toutes sont
    /// décodées, mais seule la dernière est recadrée). Pas plus de 12 recadrages par seconde : entre deux, on ne touche à rien.
    /// À la fin du fichier, l'image reste affichée jusqu'à la fin de sa durée, puis la lecture repart du début.
    /// </summary>
    public bool Advance(TimeSpan elapsed)
    {
        if (_reader is null || _broken) return false;

        var now = Max(elapsed, TimeSpan.Zero);
        if (now < _lastCrop) _lastCrop = TimeSpan.Zero - CropInterval; // le temps a reculé : on repart
        if (now - _lastCrop < CropInterval)
        {
            SetUntil(now);
            return false;
        }

        try
        {
            ReadUpTo(now);
            if (_fresh && Crop())
            {
                _fresh = false;
                _lastCrop = now;
                _version++;
                SetUntil(now);
                return true;
            }
        }
        catch (Exception ex)
        {
            Fail(Describe(ex)); // le fichier a disparu ou le décodeur a lâché en route : l'image actuelle reste figée
        }

        SetUntil(now);
        return false;
    }

    /// <summary>
    /// Lit les échantillons jusqu'à celui qui couvre <paramref name="now"/>, en bouclant à la fin du fichier. Si la position
    /// voulue dépasse de plus de <see cref="MaxLag"/> celle qui est lue, la lecture ne rattrape pas : elle reprend là où elle
    /// en était (l'origine du temps est décalée, sans recherche dans le fichier).
    /// </summary>
    private void ReadUpTo(TimeSpan now)
    {
        while (!_broken && !_still)
        {
            var media = now - _offset;
            if (_ended)
            {
                if (media < _loopLength) return; // la dernière image dure encore

                // Retour au début, sans passer par une image noire : l'image actuelle reste jusqu'au prochain échantillon.
                // Si plusieurs tours se sont écoulés d'un coup (veille), on saute les tours entiers.
                _offset += TimeSpan.FromTicks(media.Ticks / _loopLength.Ticks * _loopLength.Ticks);
                _reader!.SetCurrentPosition(0);
                _ended = false;
                _frameEnd = TimeSpan.Zero;
                _tourSamples = 0;
                continue;
            }

            var lag = media - _frameEnd;
            if (lag > MaxLag)
            {
                _offset += lag; // pause : la position voulue redevient la fin de l'image à l'écran
                media = _frameEnd;
            }

            if (media < _frameEnd) return;
            ReadNext();
        }
    }

    /// <summary>
    /// Lit le prochain échantillon et le garde à la place du précédent. Fin du fichier : note la durée du tour.
    /// La fin d'une image est son horodatage plus sa durée (la durée de l'échantillon, sinon celle de la cadence).
    /// </summary>
    private void ReadNext()
    {
        ReadFault?.Invoke();
        for (int empty = 0; empty < MaxEmptyReads; empty++)
        {
            var sample = _reader!.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out long time);

            if (flags.HasFlag(SourceReaderFlag.CurrentMediaTypeChanged)) ReadFormat();

            if (flags.HasFlag(SourceReaderFlag.Error))
            {
                sample?.Dispose();
                Fail("le lecteur Media Foundation a signalé une erreur de lecture");
                return;
            }

            if (flags.HasFlag(SourceReaderFlag.EndOfStream))
            {
                sample?.Dispose();
                if (_frameEnd <= TimeSpan.Zero)
                {
                    Fail("fin du fichier atteinte sans aucune image"); // ou revenu au début sans rien lire
                    return;
                }
                _loopLength = _frameEnd;
                _ended = true;
                if (_tourSamples == 1) Freeze(); // un tour d'une seule image : une image fixe
                return;
            }

            if (sample is null) continue; // trou dans le flux (tick) : on lit la suite

            SamplesRead++;
            _tourSamples++;
            var start = TimeSpan.FromTicks(time);
            _origin ??= start;
            var duration = SampleDuration(sample);
            _sample?.Dispose();
            _sample = sample;
            _frameEnd = start - _origin.Value + duration;
            _fresh = true;
            return;
        }

        Fail($"{MaxEmptyReads} échantillons vides de suite"); // que des trous : on ne tourne pas dans le vide
    }

    /// <summary>
    /// La vidéo n'a qu'une image : elle devient une image fixe. Plus de réveil de la boucle (<see cref="IsAnimated"/> est faux,
    /// <see cref="UntilNextFrame"/> est null, <see cref="Version"/> ne bouge plus) et le lecteur est libéré. L'image à l'écran
    /// a ses propres pixels : elle n'en dépend pas.
    /// </summary>
    private void Freeze()
    {
        _still = true;
        _sample?.Dispose();
        _sample = null;
        _reader?.Dispose();
        _reader = null;
    }

    private TimeSpan SampleDuration(IMFSample sample)
    {
        try
        {
            long ticks = sample.SampleDuration;
            if (ticks > 0) return TimeSpan.FromTicks(ticks);
        }
        catch (Exception)
        {
            // Pas de durée sur cet échantillon.
        }
        return _defaultDuration;
    }

    /// <summary>Lit la première image au moment de l'ouverture : l'image 0 est à l'écran avant le premier <see cref="Advance"/>.</summary>
    private bool ReadFirstFrame()
    {
        ReadFormat();
        ReadNext();
        if (!_fresh || _broken || !Crop()) return false;
        _fresh = false;
        _lastCrop = TimeSpan.Zero;
        SetUntil(TimeSpan.Zero);
        return true;
    }

    /// <summary>Taille, foulée et cadence du format de sortie.</summary>
    private void ReadFormat()
    {
        using var type = _reader!.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);

        ulong size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        _width = (int)(size >> 32);
        _height = (int)(size & 0xFFFFFFFF);
        if (_width <= 0 || _height <= 0 || _width > BackgroundSupport.MaxSide || _height > BackgroundSupport.MaxSide)
        {
            throw new InvalidDataException("taille de vidéo invalide");
        }

        // Foulée en octets par ligne ; négative pour une image de bas en haut. Absente : on suppose le RGB classique
        // de Windows, de bas en haut.
        _stride = TryGet(() => (int)type.GetUInt32(MediaTypeAttributeKeys.DefaultStride)) ?? -_width * 4;
        _area = VisibleArea(type, _width, _height);

        // Cadence en images par seconde, sous forme de fraction (numérateur dans les 32 bits hauts).
        if (TryGet(() => type.GetUInt64(MediaTypeAttributeKeys.FrameRate)) is { } rate && rate >> 32 != 0 && (rate & 0xFFFFFFFF) != 0)
        {
            _defaultDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * (long)(rate & 0xFFFFFFFF) / (long)(rate >> 32));
        }
    }

    /// <summary>
    /// Zone visible annoncée par le format (MFVideoArea : décalages X et Y puis largeur et hauteur, en entiers de 16 bits +
    /// fraction), ou tout le format si elle manque ou déborde. Sans elle, les lignes de bourrage d'un décodeur se verraient.
    /// </summary>
    private static SKRectI VisibleArea(IMFMediaType type, int width, int height)
    {
        var whole = new SKRectI(0, 0, width, height);
        byte[] b;
        try
        {
            b = type.GetBlob(MediaTypeAttributeKeys.MinimumDisplayAperture);
        }
        catch (Exception)
        {
            return whole; // pas de zone annoncée : tout le format est visible
        }
        if (b is not { Length: >= 16 }) return whole;

        int x = BitConverter.ToInt16(b, 2), y = BitConverter.ToInt16(b, 6);
        int w = BitConverter.ToInt32(b, 8), h = BitConverter.ToInt32(b, 12);
        return x >= 0 && y >= 0 && w > 0 && h > 0 && x + w <= width && y + h <= height ? SKRectI.Create(x, y, w, h) : whole;
    }

    /// <summary>Une propriété absente du format fait lever l'interop : ici elle rend simplement null.</summary>
    private static T? TryGet<T>(Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    /// <summary>
    /// Recadre l'échantillon courant dans l'image d'écran, en lisant directement ses pixels (sans copie). Faux si l'échantillon est
    /// inutilisable (rien n'est alors touché). Si le dessin lève une exception en route, <see cref="Current"/> reste un objet
    /// valide, jamais libéré, mais il partage les pixels de l'image d'écran : il peut alors montrer un recadrage à moitié refait.
    /// </summary>
    private bool Crop()
    {
        int rowBytes = Math.Abs(_stride);
        if (_sample is null || rowBytes < _width * 4) return false;

        using var buffer = _sample.ConvertToContiguousBuffer();
        buffer.Lock(out var data, out _, out int length);
        try
        {
            if (data == IntPtr.Zero || length < rowBytes * _height) return false;

            // Une foulée négative veut dire que la mémoire commence par la ligne du bas : c'est l'orientation EXIF
            // « miroir vertical ». La zone visible se compte depuis le haut de l'image, donc depuis la fin de la mémoire.
            bool upsideDown = _stride < 0;
            int firstRow = upsideDown ? _height - _area.Top - _area.Height : _area.Top;
            var start = data + firstRow * rowBytes + _area.Left * 4;

            // RGB32 : 4 octets par pixel dont l'octet d'alpha vaut 0 : Opaque, pour qu'il soit ignoré.
            var info = new SKImageInfo(_area.Width, _area.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var pixels = new SKPixmap(info, start, rowBytes);
            using var source = SKImage.FromPixels(pixels);
            _canvas!.Clear(SKColors.Black);
            BackgroundSupport.DrawCover(_canvas, source, upsideDown ? SKEncodedOrigin.BottomLeft : SKEncodedOrigin.TopLeft);

            // Skia recopie telles quelles les images opaques : l'alpha à 0 de Media Foundation arriverait dans l'écran. On le
            // remplace par du noir opaque sous l'image (DstOver ne touche pas aux couleurs déjà posées).
            using var fill = new SKPaint { Color = SKColors.Black, BlendMode = SKBlendMode.DstOver };
            _canvas.DrawRect(0, 0, GridGeometry.Width, GridGeometry.Height, fill);
        }
        finally
        {
            buffer.Unlock();
        }

        using var screenPixels = _screen!.PeekPixels();
        var next = SKImage.FromPixels(screenPixels);
        _current?.Dispose();
        _current = next;
        return true;
    }

    /// <summary>
    /// Délai avant que l'image puisse changer : la fin de l'image à l'écran, mais pas avant le prochain recadrage permis.
    /// Au bout du fichier, la fin du tour. Jamais moins d'une milliseconde.
    /// </summary>
    private void SetUntil(TimeSpan now)
    {
        var frame = (_ended ? _loopLength : _frameEnd) - (now - _offset);
        var crop = _lastCrop + CropInterval - now;
        _until = Max(TimeSpan.FromMilliseconds(1), Max(frame, crop));
    }

    /// <summary>
    /// La lecture n'est plus possible : la dernière image décodée reste à l'écran, figée, et plus rien ne bouge (le fond du thème
    /// n'est pas repris). Un seul avertissement, avec la cause. À l'ouverture, <c>_warn</c> est encore vide : <see cref="TryOpen"/>
    /// avertit lui-même, avec la cause gardée ici.
    /// </summary>
    private void Fail(string cause)
    {
        if (_broken) return;
        _broken = true;
        _failure = cause;
        _warn?.Invoke($"Lecture de la vidéo interrompue : {_path} ({cause}). L'image reste figée sur la dernière image décodée.");
    }

    /// <summary>Nombre d'échantillons (images) lus depuis l'ouverture. Pour les tests : le rattrapage d'un retard doit rester borné.</summary>
    internal int SamplesRead { get; private set; }

    /// <summary>Pour les tests : appelé avant chaque lecture d'échantillon ; s'il lève, la lecture échoue comme si le décodeur lâchait.</summary>
    internal Action? ReadFault { get; set; }

    /// <summary>Nombre de flux du fichier, et parmi eux ceux que le lecteur décode. Pour les tests : un seul, la vidéo.</summary>
    internal int StreamCount => CountStreams(selectedOnly: false);

    internal int SelectedStreamCount => CountStreams(selectedOnly: true);

    private int CountStreams(bool selectedOnly)
    {
        int count = 0;
        for (int index = 0; _reader is not null && index < 16; index++)
        {
            try
            {
                bool selected = _reader.GetStreamSelection(index); // lève au-delà du dernier flux
                if (selected || !selectedOnly) count++;
            }
            catch (Exception)
            {
                break; // plus de flux à cet index
            }
        }
        return count;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null!;
        _canvas?.Dispose();
        _canvas = null;
        _screen?.Dispose();
        _screen = null;
        _sample?.Dispose();
        _sample = null;
        _reader?.Dispose();
        _reader = null;
    }
}
