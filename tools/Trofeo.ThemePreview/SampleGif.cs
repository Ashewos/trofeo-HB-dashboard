using SkiaSharp;

namespace Trofeo.ThemePreview;

/// <summary>
/// Fabrique un GIF89a animé (96 × 24 px, 4 images de 8 centièmes de seconde, boucle infinie) : des vagues violettes et
/// turquoise qui se décalent d'une image à l'autre. Encodeur LZW volontairement naïf (seulement des codes littéraux,
/// avec un code d'effacement assez souvent pour que la taille des codes ne change jamais), repris de l'aide de test
/// <c>TinyGif</c> de Core.Tests. Le résultat est un GIF valide, lu tel quel par SKCodec.
/// </summary>
internal static class SampleGif
{
    private const int Width = 96, Height = 24, Frames = 4, PaletteBits = 6, DelayCs = 8;

    public static void Write(string path)
    {
        var palette = Palette();
        using var ms = new MemoryStream();
        void Bytes(params byte[] b) => ms.Write(b, 0, b.Length);
        void U16(int v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)(v >> 8)); }

        Bytes("GIF89a"u8.ToArray());
        U16(Width); U16(Height);
        Bytes((byte)(0x80 | ((PaletteBits - 1) << 4) | (PaletteBits - 1)), 0, 0); // table globale de 2^PaletteBits couleurs
        foreach (var c in palette)
        {
            Bytes(c.Red, c.Green, c.Blue);
        }

        Bytes(0x21, 0xFF, 11);
        Bytes("NETSCAPE2.0"u8.ToArray());
        Bytes(3, 1, 0, 0, 0); // boucle infinie

        for (var f = 0; f < Frames; f++)
        {
            Bytes(0x21, 0xF9, 4, 0x04); // pas de transparence, « ne pas disposer » : chaque image est autonome
            U16(DelayCs);
            Bytes(0, 0);

            Bytes(0x2C);
            U16(0); U16(0); U16(Width); U16(Height);
            Bytes(0x00, PaletteBits);

            var data = Lzw(Pixels(f), PaletteBits);
            for (var i = 0; i < data.Length; i += 255)
            {
                var n = Math.Min(255, data.Length - i);
                ms.WriteByte((byte)n);
                ms.Write(data, i, n);
            }

            Bytes(0);
        }

        Bytes(0x3B);
        File.WriteAllBytes(path, ms.ToArray());
    }

    // Du violet profond au violet vif puis au turquoise.
    private static SKColor[] Palette()
    {
        SKColor[] stops = [SKColor.Parse("#1B1140"), SKColor.Parse("#7B3FC4"), SKColor.Parse("#3AD0C8")];
        var count = 1 << PaletteBits;
        var palette = new SKColor[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)(count - 1) * (stops.Length - 1);
            var k = Math.Min((int)t, stops.Length - 2);
            palette[i] = Mix(stops[k], stops[k + 1], t - k);
        }

        return palette;
    }

    private static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t), (byte)(a.Blue + (b.Blue - a.Blue) * t));

    // Une vague en diagonale qui avance d'un cinquième de période par image.
    private static int[] Pixels(int frame)
    {
        var pixels = new int[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var phase = x / (double)Width + y / (double)Height * 0.35 - frame * 0.2;
                var level = 0.5 + 0.5 * Math.Sin(phase * 2 * Math.PI);
                pixels[y * Width + x] = Math.Clamp((int)(level * (1 << PaletteBits)), 0, (1 << PaletteBits) - 1);
            }
        }

        return pixels;
    }

    private static byte[] Lzw(int[] pixels, int minCode)
    {
        int clear = 1 << minCode, end = clear + 1, codeSize = minCode + 1;
        var perClear = Math.Max(1, clear - 3);

        var output = new List<byte>();
        int acc = 0, accBits = 0;
        void Emit(int code)
        {
            acc |= code << accBits;
            accBits += codeSize;
            while (accBits >= 8) { output.Add((byte)(acc & 0xFF)); acc >>= 8; accBits -= 8; }
        }

        Emit(clear);
        for (var i = 0; i < pixels.Length; i++)
        {
            if (i > 0 && i % perClear == 0) Emit(clear);
            Emit(pixels[i]);
        }

        Emit(end);
        if (accBits > 0) output.Add((byte)(acc & 0xFF));
        return output.ToArray();
    }
}
