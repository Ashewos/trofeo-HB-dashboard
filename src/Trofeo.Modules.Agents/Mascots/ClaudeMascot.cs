using SkiaSharp;

namespace Trofeo.Modules.Agents;

/// <summary>
/// La mascotte de Claude Code, « Clawd » : un petit bloc orange à deux yeux, quatre pattes et deux bras.
/// Scène de 24 × 16 pixels de sprite ; le personnage (16 × 10) est posé en bas et centré, les accessoires occupent le reste.
/// </summary>
public static class ClaudeMascot
{
    // Le personnage dans la scène : colonnes 4 à 19, rangées 6 à 15.
    private const int BodyX = 4;
    private const int BodyY = 6;

    // Couleurs du thème, reprises en dur : le module de rendu n'est pas encore là.
    private static readonly SKColor Orange = SKColor.Parse("#D97757");
    private static readonly SKColor Accent = SKColor.Parse("#7FD1C7");
    private static readonly SKColor Warning = SKColor.Parse("#F2B84B");
    private static readonly SKColor Text = SKColor.Parse("#ECEEF0");

    private static readonly Dictionary<char, SKColor> Palette = new()
    {
        ['o'] = Orange,
        ['k'] = SKColor.Parse("#1A1A1A"),
        ['y'] = Warning,
        ['g'] = SKColor.Parse("#8C929C"),
        ['w'] = Text,
    };

    // La pose de repos : yeux en colonnes 5 et 10 (rangées 3 et 4), centrés sur le corps (colonnes 2 à 13),
    // bras en rangées 4 et 5, pattes en rangées 8 et 9.
    private static readonly string[] RestRows =
    [
        "................",
        "..oooooooooooo..",
        "..oooooooooooo..",
        "..oookooookooo..",
        "oooookooookooooo",
        "oooooooooooooooo",
        "..oooooooooooo..",
        "..oooooooooooo..",
        "..o.o......o.o..",
        "..o.o......o.o..",
    ];

    // Dernière rangée des pattes : à plat, ou une paire levée (la gauche en 1, la droite en 2) pendant que l'autre pose le pied.
    private static readonly string[] Feet =
    [
        "..o.o......o.o..",
        "...........o.o..",
        "..o.o...........",
    ];

    /// <summary>Définition de la mascotte : pose de repos, sommeil et clips par état.</summary>
    public static MascotDefinition Definition { get; } = Build();

    private static MascotDefinition Build()
    {
        var rest = new Pose(1000, Figure());
        var sleep = new Pose(1000, [.. Figure(eyes: Eyes.Closed), .. SleepingZ()]);

        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>>
        {
            [MascotState.Idle] = [Walk(), LookAround()],
            [MascotState.Working] = [Curl(), Press()],
            [MascotState.Waiting] = [Wave(), WaveAndJump()],
            [MascotState.Done] = [Confetti()],
        };

        return new MascotDefinition("Claude", 24, 16, rest, sleep, clips);
    }

    // ---------------------------------------------------------------- Inactif

    // Huit pas de 110 ms vers la droite puis le retour, pattes alternées, le regard dans le sens de la marche.
    private static Clip Walk()
    {
        int[] steps = [1, 2, 3, 4, 3, 2, 1, 0];
        var poses = steps.Select((dx, i) =>
            new Pose(110, Figure(dx: dx, feet: i % 2 == 0 ? 1 : 2, look: i < 4 ? 1 : -1)));
        return new Clip("balade", [.. poses]);
    }

    // Regard à gauche, au centre, à droite, puis un clignement.
    private static Clip LookAround() => new("regarde",
    [
        new Pose(400, Figure(look: -1)),
        new Pose(300, Figure()),
        new Pose(400, Figure(look: 1)),
        new Pose(120, Figure(eyes: Eyes.Closed)),
    ]);

    // ---------------------------------------------------------------- Travaille

    // Le bras droit monte et descend le long du corps, l'haltère posé sur la main : 4 poses, jouées 3 fois.
    private static Clip Curl()
    {
        int[] armRows = [4, 3, 1, 3];
        var cycle = armRows.Select(row => new Pose(140,
        [
            .. Figure(rightArm: false),
            Still(Px("oo", "oo"), BodyX + 14, BodyY + row),
            Still(Dumbbell(), BodyX + 14, BodyY + row - 2),
        ])).ToList();
        return new Clip("haltère", [.. cycle, .. cycle, .. cycle]);
    }

    // L'haltère au-dessus de la tête, tenu à deux bras : il monte de 2 pixels (OutQuad), tient, puis redescend.
    private static Clip Press()
    {
        Pose At(int fromRow, int toRow, Ease ease) => new(120,
        [
            .. Figure(),
            new Layer(OverheadDumbbell(), new SKPointI(BodyX + 5, BodyY + fromRow), new SKPointI(BodyX + 5, BodyY + toRow)),
        ], ease);

        return new Clip("développé",
        [
            At(-1, -1, Ease.Linear),
            At(-1, -2, Ease.OutQuad),
            At(-2, -3, Ease.OutQuad),
            At(-3, -3, Ease.Linear),
            At(-3, -2, Ease.InQuad),
            At(-2, -1, Ease.InQuad),
        ]);
    }

    // ---------------------------------------------------------------- Attend

    // Le bras levé tient un mât blanc et son drapeau jaune, qui ondule entre deux formes.
    private static Clip Wave()
    {
        var poses = Enumerable.Range(0, 6).Select(i => new Pose(130, Flag(wave: i % 2)));
        return new Clip("drapeau", [.. poses]);
    }

    // Le même drapeau, avec un saut de 2 pixels : montée en OutQuad, retombée en InQuad.
    private static Clip WaveAndJump() => new("saut-drapeau",
    [
        new Pose(130, Flag(wave: 0)),
        new Pose(130, Jump(Flag(wave: 1), 0, -2), Ease.OutQuad),
        new Pose(100, Jump(Flag(wave: 0), -2, -2)),
        new Pose(130, Jump(Flag(wave: 1), -2, 0), Ease.InQuad),
        new Pose(130, Flag(wave: 0)),
        new Pose(130, Flag(wave: 1)),
    ]);

    private static List<Layer> Flag(int wave) =>
    [
        .. Figure(rightArm: false),
        Still(Px("oo", "oo"), BodyX + 14, BodyY + 2),
        Still(Px("w", "w", "w", "w", "w", "w", "w", "w"), BodyX + 14, BodyY - 3),
        Still(wave == 0
            ? Px("yyyyy", "yyyyy", "yyy..")
            : Px("yyy..", "yyyyy", "yyyyy"), BodyX + 15, BodyY - 3),
    ];

    // Tous les calques montent ensemble, d'un décalage vertical de départ à un décalage d'arrivée.
    private static List<Layer> Jump(IEnumerable<Layer> layers, int fromDy, int toDy) =>
        [.. layers.Select(l => new Layer(l.Sprite, new SKPointI(l.From.X, l.From.Y + fromDy), new SKPointI(l.To.X, l.To.Y + toDy)))];

    // ---------------------------------------------------------------- Terminé

    // Les deux bras en l'air une pose sur deux, et 24 confettis qui partent du haut du corps en ralentissant.
    private static Clip Confetti()
    {
        const int steps = 10;
        Sprite[] dots = [Particles.Dot(Accent), Particles.Dot(Warning), Particles.Dot(Orange), Particles.Dot(Text)];
        var area = new SKRectI(0, 0, 24, 7);
        var origin = new SKPointI(BodyX + 8, BodyY);

        var poses = Enumerable.Range(0, steps).Select(step =>
        {
            var armsUp = step % 2 == 0;
            var layers = new List<Layer>(Figure(leftArm: !armsUp, rightArm: !armsUp));
            if (armsUp)
            {
                layers.Add(Still(Px("oo", "oo"), BodyX, BodyY + 2));
                layers.Add(Still(Px("oo", "oo"), BodyX + 14, BodyY + 2));
            }

            layers.AddRange(Particles.Burst(dots, 24, 20_261_025, origin, area, step, steps, Ease.OutQuad));
            return new Pose(100, layers);
        });
        return new Clip("confettis", [.. poses]);
    }

    // ---------------------------------------------------------------- Pièces du dessin

    private enum Eyes
    {
        Open,
        Closed,
    }

    // Le personnage : corps (avec ou sans bras), puis les yeux. « dx » décale tout le corps, « feet » choisit les pattes,
    // « look » décale les yeux ouverts d'un pixel vers la gauche (−1) ou la droite (1).
    private static List<Layer> Figure(int dx = 0, bool leftArm = true, bool rightArm = true, int feet = 0, Eyes eyes = Eyes.Open, int look = 0)
    {
        var rows = RestRows.Select(row => row.Replace('k', 'o')).ToArray();
        rows[9] = Feet[feet];
        foreach (var r in new[] { 4, 5 })
        {
            rows[r] = (leftArm ? rows[r][..2] : "..") + rows[r][2..14] + (rightArm ? rows[r][14..] : "..");
        }

        var x = BodyX + dx;
        var layers = new List<Layer> { Still(new Sprite(rows, Palette), x, BodyY) };
        if (eyes == Eyes.Open)
        {
            // Colonnes 5 et 10 au repos, rangées 3 et 4.
            layers.Add(Still(Px("k", "k"), x + 5 + look, BodyY + 3));
            layers.Add(Still(Px("k", "k"), x + 10 + look, BodyY + 3));
        }
        else
        {
            // Yeux fermés : une rangée horizontale de 2 pixels par œil (colonnes 4-5 et 10-11), à la rangée basse des yeux.
            layers.Add(Still(Px("kk"), x + 4, BodyY + 4));
            layers.Add(Still(Px("kk"), x + 10, BodyY + 4));
        }

        return layers;
    }

    // Trois « z » d'un pixel en escalier au-dessus de la tête.
    private static List<Layer> SleepingZ() =>
    [
        Still(Px("w"), 15, 5),
        Still(Px("w"), 17, 3),
        Still(Px("w"), 19, 1),
    ];

    // Haltère de 5 × 2 : un disque gris de 2 pixels de haut à chaque bout, la barre blanche entre les deux.
    private static Sprite Dumbbell() => Px("g...g", "gwwwg");

    // Haltère tenu à deux bras : l'haltère, puis deux bras de 2 pixels qui le relient à la tête.
    private static Sprite OverheadDumbbell() => Px("g...g", "gwwwg", "o...o", "o...o");

    private static Layer Still(Sprite sprite, int x, int y) => new(sprite, new SKPointI(x, y), new SKPointI(x, y));

    private static Sprite Px(params string[] rows) => new(rows, Palette);
}
