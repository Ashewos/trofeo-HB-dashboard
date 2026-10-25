using SkiaSharp;

namespace Trofeo.Core.Tests;

/// <summary>
/// Écrit à la main un GIF89a animé, minuscule (4 × 4 px), pour les tests de fonds animés : une couleur unie par image,
/// table de couleurs globale, boucle infinie (NETSCAPE2.0), une extension de contrôle graphique par image
/// (délai en centièmes de seconde, sans transparence : chaque image est autonome).
/// Encodeur LZW volontairement naïf : seulement des codes littéraux, avec un code d'effacement assez souvent pour que
/// la taille des codes ne grandisse jamais. Le résultat est un GIF valide, lu tel quel par SKCodec.
/// </summary>
internal static class TinyGif
{
    public const int Size = 4;

    public static void Write(string path, IReadOnlyList<SKColor> frames, IReadOnlyList<int> delaysCs) =>
        WritePatches(path, frames.Select(c => (c, 0, 0, Size, Size)).ToList(), delaysCs);

    /// <summary>
    /// Variante où chaque image ne couvre qu'un rectangle (x, y, largeur, hauteur) et laisse le reste de l'écran tel
    /// quel (élimination « ne pas disposer ») : l'image dépend alors de la précédente (RequiredFrame ≥ 0).
    /// </summary>
    public static void WritePatches(string path, IReadOnlyList<(SKColor Color, int X, int Y, int W, int H)> frames,
        IReadOnlyList<int> delaysCs)
    {
        if (frames.Count != delaysCs.Count) throw new ArgumentException("Un délai par image.");

        var palette = frames.Select(f => f.Color).Distinct().ToList();
        int bits = 1;
        while ((1 << bits) < palette.Count) bits++;
        int minCode = Math.Max(2, bits);

        using var ms = new MemoryStream();
        void Bytes(params byte[] b) => ms.Write(b, 0, b.Length);
        void U16(int v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)(v >> 8)); }

        Bytes("GIF89a"u8.ToArray());
        U16(Size); U16(Size);
        Bytes((byte)(0x80 | ((bits - 1) << 4) | (bits - 1)), 0, 0); // table globale, taille 2^bits
        for (int i = 0; i < (1 << bits); i++)
        {
            var c = i < palette.Count ? palette[i] : SKColors.Black;
            Bytes(c.Red, c.Green, c.Blue);
        }

        // Boucle infinie.
        Bytes(0x21, 0xFF, 11);
        Bytes("NETSCAPE2.0"u8.ToArray());
        Bytes(3, 1, 0, 0, 0);

        for (int f = 0; f < frames.Count; f++)
        {
            Bytes(0x21, 0xF9, 4, 0x04); // pas de transparence, « ne pas disposer »
            U16(delaysCs[f]);
            Bytes(0, 0);

            Bytes(0x2C);
            U16(frames[f].X); U16(frames[f].Y); U16(frames[f].W); U16(frames[f].H);
            Bytes(0x00, (byte)minCode);

            var data = Lzw(Enumerable.Repeat(palette.IndexOf(frames[f].Color), frames[f].W * frames[f].H).ToArray(), minCode);
            for (int i = 0; i < data.Length; i += 255)
            {
                int n = Math.Min(255, data.Length - i);
                ms.WriteByte((byte)n);
                ms.Write(data, i, n);
            }
            Bytes(0); // fin des données
        }

        Bytes(0x3B);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static byte[] Lzw(int[] pixels, int minCode)
    {
        int clear = 1 << minCode, end = clear + 1, codeSize = minCode + 1;
        // Après un effacement, la table compte clear + 2 entrées : on peut en ajouter (2^codeSize - clear - 2) avant que
        // la taille des codes change. Un code d'effacement tous les (clear - 3) littéraux reste donc largement en deçà.
        int perClear = Math.Max(1, clear - 3);

        var output = new List<byte>();
        int acc = 0, accBits = 0;
        void Emit(int code)
        {
            acc |= code << accBits;
            accBits += codeSize;
            while (accBits >= 8) { output.Add((byte)(acc & 0xFF)); acc >>= 8; accBits -= 8; }
        }

        Emit(clear);
        for (int i = 0; i < pixels.Length; i++)
        {
            if (i > 0 && i % perClear == 0) Emit(clear);
            Emit(pixels[i]);
        }
        Emit(end);
        if (accBits > 0) output.Add((byte)(acc & 0xFF));
        return output.ToArray();
    }
}
