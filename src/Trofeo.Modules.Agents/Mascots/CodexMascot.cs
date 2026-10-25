using SkiaSharp;

namespace Trofeo.Modules.Agents;

/// <summary>
/// La mascotte de Codex : le « pet » officiel redessiné, un nuage bleu arrondi avec un écran sombre où s'affiche <c>&gt;_</c>.
/// Scène de 24 × 16 pixels de sprite ; le nuage (16 × 10) est posé en bas et centré, les accessoires occupent le reste.
/// </summary>
public static class CodexMascot
{
    // Le nuage dans la scène : colonnes 4 à 19, rangées 6 à 15.
    private const int BodyX = 4;
    private const int BodyY = 6;

    private static readonly Dictionary<char, SKColor> Palette = new()
    {
        ['b'] = SKColor.Parse("#7AA7FF"),
        ['l'] = SKColor.Parse("#B8D0FF"),
        ['s'] = SKColor.Parse("#1B2233"),
        ['t'] = SKColor.Parse("#E9EEF5"),
        ['y'] = SKColor.Parse("#F2B84B"),
        ['h'] = SKColor.Parse("#FF7A9A"),
        ['g'] = SKColor.Parse("#8C929C"),
    };

    // Le nuage : un reflet « l » en haut à gauche, un écran « s » de 10 × 5 au centre (colonnes 3 à 12, rangées 2 à 6).
    private static readonly string[] CloudRows =
    [
        ".....llbbbb.....",
        "..lbbbbbbbbbbb..",
        ".lbssssssssssbb.",
        "lbbssssssssssbbb",
        "bbbssssssssssbbb",
        "bbbssssssssssbbb",
        ".bbssssssssssbb.",
        ".bbbbbbbbbbbbbb.",
        "..bbbbbbbbbbbb..",
        "....bbbbbbbb....",
    ];

    /// <summary>Définition de la mascotte : pose de repos, sommeil et clips par état.</summary>
    public static MascotDefinition Definition { get; } = Build();

    private static MascotDefinition Build()
    {
        var rest = new Pose(1000, [Cloud(), .. Prompt()]);
        var sleep = new Pose(1000, [Cloud(), .. Dozing(), .. SleepingZ(3)]);

        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>>
        {
            [MascotState.Idle] = [Sleeps(), Hums()],
            [MascotState.Working] = [Types(), TypesFast()],
            [MascotState.Waiting] = [Question(), Exclamation()],
            [MascotState.Done] = [Hearts()],
        };

        return new MascotDefinition("Codex", 24, 16, rest, sleep, clips);
    }

    // ---------------------------------------------------------------- Inactif

    // Écran sombre avec « -_ », et trois « z » qui montent en escalier, un de plus à chaque pose puis effacés dans l'ordre.
    private static Clip Sleeps() =>
        new("dort", [.. new[] { 1, 2, 3, 3, 4, 5 }.Select(count => new Pose(300, [Cloud(), .. Dozing(), .. SleepingZ(count)]))]);

    // Une note qui monte en diagonale, pendant que le nuage se balance d'un pixel.
    private static Clip Hums()
    {
        int[] sway = [0, 1, 1, 0, 0, -1, -1, 0];
        var note = Px(".tt", ".t.", ".t.", "tt.");
        var poses = sway.Select((dx, i) =>
        {
            // De (16, 4) à (21, 0) en 8 poses : une marche en diagonale, sans glissement.
            var x = 16 + (int)Math.Round(5 * i / 7.0, MidpointRounding.AwayFromZero);
            var y = 4 - (int)Math.Round(4 * i / 7.0, MidpointRounding.AwayFromZero);
            return new Pose(150, [Cloud(dx), .. Prompt(dx), Still(note, x, y)]);
        });
        return new Clip("fredonne", [.. poses]);
    }

    // ---------------------------------------------------------------- Travaille

    // Un portable devant le nuage, deux mains qui se relaient, et le curseur de « >_ » qui clignote.
    private static Clip Types()
    {
        bool[] cursor = [true, true, false, true, true, false];
        var poses = cursor.Select((on, i) => new Pose(110, [Cloud(), .. Prompt(cursorOn: on), .. Laptop(i)]));
        return new Clip("tape", [.. poses]);
    }

    // Même scène, plus vite, avec des lignes de texte qui défilent dans l'écran (10 rangées, une de plus à chaque pose).
    private static Clip TypesFast()
    {
        string[] tape =
        [
            "ttttt...",
            "........",
            "tt.tttt.",
            "........",
            "ttttttt.",
            "........",
            "ttt.tt..",
            "........",
            "tttt....",
            "........",
        ];

        var poses = Enumerable.Range(0, tape.Length).Select(i =>
        {
            // Fenêtre de 3 rangées dans l'écran (colonnes 4 à 11, rangées 3 à 5).
            var window = Enumerable.Range(0, 3).Select(r => tape[(i + r) % tape.Length]).ToArray();
            return new Pose(85, [Cloud(), Still(Px(window), BodyX + 4, BodyY + 3), .. Laptop(i)]);
        });
        return new Clip("tape-vite", [.. poses]);
    }

    // Portable de 8 × 3 devant le bas du nuage, et deux mains de 2 × 2 de chaque côté, l'une en haut quand l'autre est en bas.
    private static List<Layer> Laptop(int pose)
    {
        var left = pose % 2 == 0 ? 12 : 13;
        var right = pose % 2 == 0 ? 13 : 12;
        return
        [
            Still(Px("gggggggg", "gggggggg", ".gggggg."), 8, 13),
            Still(Px("ll", "ll"), 6, left),
            Still(Px("ll", "ll"), 16, right),
        ];
    }

    // ---------------------------------------------------------------- Attend

    // Un « ? » jaune au-dessus du nuage, qui rebondit de 1 pixel : 3 rebonds.
    private static Clip Question()
    {
        var mark = Px("yyy", "..y", ".y.", "...", ".y.");
        Pose Bounce(int from, int to, Ease ease) =>
            new(140, [Cloud(), .. Prompt(), new Layer(mark, new SKPointI(10, from), new SKPointI(10, to))], ease);

        return new Clip("question",
        [
            Bounce(1, 0, Ease.OutQuad), Bounce(0, 1, Ease.InQuad),
            Bounce(1, 0, Ease.OutQuad), Bounce(0, 1, Ease.InQuad),
            Bounce(1, 0, Ease.OutQuad), Bounce(0, 1, Ease.InQuad),
        ]);
    }

    // Un « ! » jaune qui apparaît et disparaît.
    private static Clip Exclamation()
    {
        var mark = Px("yy", "yy", "yy", "..", "yy");
        bool[] shown = [false, true, true, false, true, true];
        var poses = shown.Select(on => new Pose(130, on ? [Cloud(), .. Prompt(), Still(mark, 11, 1)] : [Cloud(), .. Prompt()]));
        return new Clip("exclamation", [.. poses]);
    }

    // ---------------------------------------------------------------- Terminé

    // Six cœurs de 3 × 3 et dix étincelles qui montent en ralentissant.
    private static Clip Hearts()
    {
        const int steps = 10;
        var heart = Px("h.h", "hhh", ".h.");
        var spark = Particles.Dot(Palette['t']);
        var area = new SKRectI(0, 0, 24, 6);

        var poses = Enumerable.Range(0, steps).Select(step => new Pose(100,
        [
            Cloud(),
            .. Prompt(),
            .. Particles.Burst([heart], 6, 20_261_026, new SKPointI(BodyX + 6, BodyY - 2), area, step, steps, Ease.OutQuad),
            .. Particles.Burst([spark], 10, 20_261_027, new SKPointI(BodyX + 7, BodyY - 1), area, step, steps, Ease.OutQuad),
        ]));
        return new Clip("cœurs", [.. poses]);
    }

    // ---------------------------------------------------------------- Pièces du dessin

    private static Layer Cloud(int dx = 0) => Still(new Sprite(CloudRows, Palette), BodyX + dx, BodyY);

    // « >_ » : un chevron de 3 pixels de haut et un trait bas de 3 pixels (colonnes 5-6 et 8-10, rangées 3 à 5 du nuage).
    private static List<Layer> Prompt(int dx = 0, bool cursorOn = true)
    {
        var layers = new List<Layer> { Still(Px("t.", ".t", "t."), BodyX + 5 + dx, BodyY + 3) };
        if (cursorOn)
        {
            layers.Add(Still(Px("ttt"), BodyX + 8 + dx, BodyY + 5));
        }

        return layers;
    }

    // « -_ » : l'écran de veille.
    private static List<Layer> Dozing() =>
    [
        Still(Px("tt"), BodyX + 5, BodyY + 4),
        Still(Px("ttt"), BodyX + 8, BodyY + 5),
    ];

    // Trois « z » de 3 × 4 en escalier vers le haut et la droite, qui apparaissent un à un puis s'effacent dans le même
    // ordre : « count » en montre 1, 2 ou 3 depuis le bas ; au-delà de 3, ils s'effacent un à un depuis le bas.
    private static List<Layer> SleepingZ(int count)
    {
        var z = Px("ttt", "..t", ".t.", "ttt");
        (int X, int Y)[] zs = [(13, 2), (17, 1), (21, 0)];
        var layers = new List<Layer>();
        for (var k = 0; k < zs.Length; k++)
        {
            // « count » de 1 à 3 : les k premiers ; de 4 à 5 : les derniers, une fois les premiers partis.
            var shown = count <= 3 ? k < count : k >= count - 3;
            if (shown)
            {
                layers.Add(Still(z, zs[k].X, zs[k].Y));
            }
        }

        return layers;
    }

    private static Layer Still(Sprite sprite, int x, int y) => new(sprite, new SKPointI(x, y), new SKPointI(x, y));

    private static Sprite Px(params string[] rows) => new(rows, Palette);
}
