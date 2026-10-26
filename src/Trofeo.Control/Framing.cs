using System.Buffers.Binary;

namespace Trofeo.Control;

internal enum FrameKind : byte { Json = 1, Image = 2 }

internal readonly record struct Frame(FrameKind Kind, byte[] Payload);

/// <summary>
/// Trame du canal : <c>int32</c> petit-boutiste, puis 1 octet de sorte, puis la charge. La longueur compte l'octet de sorte
/// et la charge. Charge d'une image : <c>int32</c> de rotation (petit-boutiste), puis le JPEG. Au-delà de
/// <see cref="MaxFrameBytes"/>, la trame est refusée (en lecture, la connexion doit être fermée).
/// </summary>
internal static class Framing
{
    public const int MaxFrameBytes = 8 * 1024 * 1024;

    private const int HeaderBytes = 5;

    public static Task WriteJsonAsync(Stream stream, ReadOnlyMemory<byte> utf8, CancellationToken ct) =>
        WriteAsync(stream, FrameKind.Json, [], utf8, ct);

    public static Task WriteImageAsync(Stream stream, int rotation, ReadOnlyMemory<byte> jpeg, CancellationToken ct)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, rotation);
        return WriteAsync(stream, FrameKind.Image, prefix, jpeg, ct);
    }

    private static async Task WriteAsync(Stream stream, FrameKind kind, byte[] prefix, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var length = 1L + prefix.Length + body.Length;
        if (length > MaxFrameBytes)
            throw new InvalidDataException($"Trame trop grosse ({length} octets).");

        var header = new byte[HeaderBytes + prefix.Length];
        BinaryPrimitives.WriteInt32LittleEndian(header, (int)length);
        header[4] = (byte)kind;
        prefix.CopyTo(header, HeaderBytes);

        // Pas de Flush : sur un tube nommé Windows, il attendrait que l'autre bout ait tout lu.
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    /// <summary>Lit une trame ; null à la fin propre du flux. Lève <see cref="InvalidDataException"/> si la trame est invalide ou trop grosse.</summary>
    public static async Task<Frame?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[HeaderBytes];
        if (!await ReadExactlyOrEndAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > MaxFrameBytes)
            throw new InvalidDataException($"Longueur de trame invalide ({length}).");
        if (header[4] is not ((byte)FrameKind.Json or (byte)FrameKind.Image))
            throw new InvalidDataException($"Sorte de trame inconnue ({header[4]}).");

        var payload = new byte[length - 1];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        return new Frame((FrameKind)header[4], payload);
    }

    public static (int Rotation, byte[] Jpeg) ParseImage(byte[] payload)
    {
        if (payload.Length < 4)
            throw new InvalidDataException("Image trop courte.");
        return (BinaryPrimitives.ReadInt32LittleEndian(payload), payload[4..]);
    }

    /// <summary>Remplit le tampon ; faux si le flux se termine avant le premier octet, exception s'il se termine au milieu.</summary>
    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0)
                    return false;
                throw new EndOfStreamException();
            }
            read += n;
        }
        return true;
    }
}
