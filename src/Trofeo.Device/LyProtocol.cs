using System.Buffers.Binary;

namespace Trofeo.Device;

/// <summary>Protocole USB « LY » de l'écran (annexe A de la spec) : poignée de main et découpage d'une image JPEG.</summary>
public static class LyProtocol
{
    public const int PacketSize = 512;
    public const int HeaderSize = 16;
    public const int PayloadSize = PacketSize - HeaderSize;
    public const int HandshakeSize = 2048;
    public const int MaxJpegSize = 450_000;

    /// <summary>2048 octets : [0]=0x02, [1]=0xFF, [8]=0x01, le reste à zéro.</summary>
    public static byte[] BuildHandshake()
    {
        var buffer = new byte[HandshakeSize];
        buffer[0] = 0x02;
        buffer[1] = 0xFF;
        buffer[8] = 0x01;
        return buffer;
    }

    /// <summary>Vrai si la réponse commence par 0x03 0xFF et que l'octet 8 vaut 0x01.</summary>
    public static bool IsValidHandshakeReply(ReadOnlySpan<byte> reply) =>
        reply.Length > 8 && reply[0] == 0x03 && reply[1] == 0xFF && reply[8] == 0x01;

    /// <summary>
    /// Découpe un JPEG en paquets de 512 octets (en-tête de 16 + charge de 496), complétés par des
    /// paquets à zéro jusqu'à un multiple de 4.
    /// </summary>
    /// <exception cref="ArgumentException">Le JPEG dépasse <see cref="MaxJpegSize"/>.</exception>
    public static byte[] Packetize(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length > MaxJpegSize)
            throw new ArgumentException($"Le JPEG dépasse {MaxJpegSize} octets ({jpeg.Length}).", nameof(jpeg));

        var count = jpeg.Length / PayloadSize + 1;
        var paddedCount = (count + 3) / 4 * 4;
        var buffer = new byte[paddedCount * PacketSize];

        for (var i = 0; i < count; i++)
        {
            var packet = buffer.AsSpan(i * PacketSize, PacketSize);
            var chunk = jpeg.Slice(i * PayloadSize, Math.Min(PayloadSize, jpeg.Length - i * PayloadSize));

            packet[0] = 0x01;
            packet[1] = 0xFF;
            BinaryPrimitives.WriteUInt32LittleEndian(packet[2..], (uint)jpeg.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[6..], (ushort)chunk.Length);
            packet[8] = 0x01;
            BinaryPrimitives.WriteUInt16LittleEndian(packet[9..], (ushort)count);
            BinaryPrimitives.WriteUInt16LittleEndian(packet[11..], (ushort)i);
            chunk.CopyTo(packet[HeaderSize..]);
        }

        return buffer;
    }
}
