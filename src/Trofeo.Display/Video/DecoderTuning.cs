using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace Trofeo.Display.Video;

/// <summary>
/// Réglages du décodeur vidéo de Media Foundation. <c>null</c> : le décodeur garde son défaut.
/// Par défaut, il prend un fil de travail par cœur logique (20 sur le PC d'Alex), chacun avec ses tampons d'images : 223 Mo
/// de mémoire propre pour une vidéo 1080p, 111 Mo pour une vidéo 1920 × 462. Un seul fil : 73 Mo et 43 Mo, un quart de
/// processeur de moins, et le même débit de décodage (115 images/s en 1080p, pour 30 demandées) : le goulot n'est pas là.
/// Mesuré avec <c>tools/Trofeo.VideoBench</c> (chiffres dans le README, « Fonds animés »). Sur un flux très lourd
/// (1080p60 à 40 Mb/s), un seul fil fait monter le pire Advance de 200 à 330 ms ; deux fils l'éviteraient pour environ 20 Mo de plus.
/// </summary>
internal sealed record DecoderTuning(int? WorkerThreads = 1)
{
    public static DecoderTuning Default { get; } = new();

    /// <summary>Pour les tests : appelé avant l'application des réglages ; s'il lève, le réglage échoue comme s'il était refusé.</summary>
    public Action? Fault { get; init; }
}

/// <summary>Applique les réglages au décodeur que le lecteur de source a chargé pour le flux vidéo.</summary>
internal static class DecoderTuner
{
    private static readonly Guid VideoDecoderCategory = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");   // MFT_CATEGORY_VIDEO_DECODER
    private static readonly Guid NumWorkerThreads = new("9561c3e8-ea9e-4435-9b1e-a93e691894d8");       // CODECAPI_AVDecNumWorkerThreads
    private static readonly Guid CodecApiId = new("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");             // IID_ICodecAPI

    /// <summary>
    /// Réglages du décodeur du flux vidéo. Rend null si tout est passé, sinon la cause du refus (le décodeur garde alors
    /// son défaut pour ce qui n'est pas passé). Ne lève jamais.
    /// </summary>
    public static string? Apply(IMFSourceReader reader, DecoderTuning tuning)
    {
        if (tuning.WorkerThreads is null && tuning.Fault is null) return null;
        try
        {
            tuning.Fault?.Invoke();
            using var readerEx = reader.QueryInterface<IMFSourceReaderEx>();
            using var decoder = FindDecoder(readerEx);
            var problems = new List<string>();
            if (tuning.WorkerThreads is { } threads) Try(problems, "nombre de fils", () => SetWorkerThreads(decoder, threads));
            return problems.Count == 0 ? null : string.Join(" ; ", problems);
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static void Try(List<string> problems, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            problems.Add($"{what} : {Describe(ex)}");
        }
    }

    private static string Describe(Exception ex) =>
        $"{(string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message.Trim())} [HRESULT 0x{ex.HResult:X8}]";

    /// <summary>Le décodeur vidéo du flux : le premier transformateur de la chaîne de la catégorie des décodeurs vidéo.</summary>
    private static IMFTransform FindDecoder(IMFSourceReaderEx readerEx)
    {
        for (int index = 0; index < 8; index++)
        {
            IMFTransform transform;
            Guid category;
            try
            {
                readerEx.GetTransformForStream((int)SourceReaderIndex.FirstVideoStream, index, out category, out transform);
            }
            catch (Exception)
            {
                break; // plus de transformateur à cet index
            }

            if (category == VideoDecoderCategory) return transform;
            transform.Dispose();
        }
        throw new InvalidOperationException("aucun décodeur vidéo dans la chaîne du lecteur");
    }

    private static void SetWorkerThreads(IMFTransform decoder, int threads)
    {
        var iid = CodecApiId;
        int hr = Marshal.QueryInterface(decoder.NativePointer, in iid, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        ICodecApi? codec = null;
        try
        {
            codec = (ICodecApi)Marshal.GetObjectForIUnknown(pointer);
            object value = threads;
            var api = NumWorkerThreads;
            Marshal.ThrowExceptionForHR(codec.SetValue(ref api, ref value));
        }
        finally
        {
            // Le RCW tient sa propre référence : sans libération explicite, le décodeur et ses tampons vivraient jusqu'au GC.
            if (codec is not null) Marshal.FinalReleaseComObject(codec);
            Marshal.Release(pointer);
        }
    }

    /// <summary>ICodecAPI (codecapi.h) : seul SetValue sert ; les méthodes d'avant gardent leur place dans la table virtuelle.</summary>
    [ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICodecApi
    {
        [PreserveSig] int IsSupported(ref Guid api);
        [PreserveSig] int IsModifiable(ref Guid api);
        [PreserveSig] int GetParameterRange(ref Guid api, out object min, out object max, out object step);
        [PreserveSig] int GetParameterValues(ref Guid api, out IntPtr values, out uint count);
        [PreserveSig] int GetDefaultValue(ref Guid api, out object value);
        [PreserveSig] int GetValue(ref Guid api, out object value);
        [PreserveSig] int SetValue(ref Guid api, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
