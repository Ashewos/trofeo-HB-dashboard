using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace Trofeo.Modules.Agents.Tests;

public sealed class MascotAnimatorTests
{
    private static readonly SKColor RestColor = new(10, 200, 10);
    private static readonly SKColor SleepColor = new(10, 10, 200);
    private static readonly SKColor ActColor = new(200, 10, 10);

    private static readonly Dictionary<char, SKColor> Palette = new()
    {
        ['r'] = RestColor,
        ['s'] = SleepColor,
        ['a'] = ActColor,
    };

    private static Sprite One(char c) => new([c.ToString()], Palette);

    private static Pose Still(char c, int ms = 100, int x = 0, int y = 0) =>
        new(ms, [new Layer(One(c), new SKPointI(x, y), new SKPointI(x, y))]);

    private static Pose Moving(int ms, SKPointI from, SKPointI to) =>
        new(ms, [new Layer(One('a'), from, to)]);

    // 3 poses de 100 ms : un clip dure 300 ms.
    private static Clip MakeClip(string name) =>
        new(name, [Still('a', 100, 1, 0), Still('a', 100, 2, 0), Still('a', 100, 3, 0)]);

    private static MascotDefinition Definition(
        IReadOnlyDictionary<MascotState, IReadOnlyList<Clip>>? clips = null,
        int sceneWidth = 8,
        int sceneHeight = 4) =>
        new(
            "test",
            sceneWidth,
            sceneHeight,
            Still('r'),
            Still('s'),
            clips ?? new Dictionary<MascotState, IReadOnlyList<Clip>>
            {
                [MascotState.Idle] = [MakeClip("blink"), MakeClip("look")],
                [MascotState.Working] = [MakeClip("type-a"), MakeClip("type-b")],
                [MascotState.Waiting] = [MakeClip("wave")],
                [MascotState.Done] = [MakeClip("cheer")],
            });

    private static (MascotAnimator Animator, FakeTimeProvider Time) Create(MascotDefinition? definition = null, int seed = 42)
    {
        var time = new FakeTimeProvider();
        return (new MascotAnimator(definition ?? Definition(), time, new Random(seed)), time);
    }

    [Fact]
    public void Animator_StartsIdleAtRest()
    {
        var (animator, _) = Create();
        Assert.Equal(MascotState.Idle, animator.State);
        Assert.False(animator.IsAnimating);
        Assert.False(animator.IsSleeping);
        Assert.Null(animator.CurrentClip);
        var wait = animator.UntilNextChange();
        Assert.InRange(wait, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void Animator_StateChange_StartsClipImmediately()
    {
        var (animator, _) = Create();
        animator.SetState(MascotState.Working);

        Assert.Equal(MascotState.Working, animator.State);
        Assert.True(animator.IsAnimating);
        Assert.StartsWith("type-", animator.CurrentClip);
        Assert.Equal(TimeSpan.FromMilliseconds(100), animator.UntilNextChange());
    }

    [Fact]
    public void Animator_SameState_HasNoEffect()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Working);
        var clip = animator.CurrentClip;
        time.Advance(TimeSpan.FromMilliseconds(150));
        var until = animator.UntilNextChange();

        animator.SetState(MascotState.Working);

        Assert.Equal(clip, animator.CurrentClip);
        Assert.Equal(until, animator.UntilNextChange());
    }

    [Fact]
    public void Animator_UntilNextChange_FollowsPoses()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Waiting);
        time.Advance(TimeSpan.FromMilliseconds(130));

        Assert.True(animator.IsAnimating);
        Assert.Equal(TimeSpan.FromMilliseconds(70), animator.UntilNextChange());
    }

    [Theory]
    [InlineData(MascotState.Idle, 4, 12)]
    [InlineData(MascotState.Working, 0.5, 2)]
    [InlineData(MascotState.Waiting, 1, 3)]
    public void Animator_AfterClip_PausesAndStopsAnimating(MascotState state, double minSeconds, double maxSeconds)
    {
        foreach (var seed in new[] { 1, 2, 3, 42, 99 })
        {
            var (animator, time) = Create(seed: seed);
            animator.SetState(MascotState.Done);
            animator.SetState(state);
            Assert.True(animator.IsAnimating);

            time.Advance(TimeSpan.FromMilliseconds(300));

            Assert.False(animator.IsAnimating);
            Assert.Null(animator.CurrentClip);
            Assert.InRange(
                animator.UntilNextChange(),
                TimeSpan.FromSeconds(minSeconds),
                TimeSpan.FromSeconds(maxSeconds));
        }
    }

    [Fact]
    public void Animator_PauseEnd_StartsNextClip()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Working);
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(animator.IsAnimating);

        time.Advance(animator.UntilNextChange());

        Assert.True(animator.IsAnimating);
        Assert.StartsWith("type-", animator.CurrentClip);
    }

    [Fact]
    public void Animator_NoImmediateRepeat()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Working);

        var names = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            while (!animator.IsAnimating)
            {
                time.Advance(animator.UntilNextChange());
            }

            names.Add(animator.CurrentClip!);
            while (animator.IsAnimating)
            {
                time.Advance(animator.UntilNextChange());
            }
        }

        Assert.Equal(20, names.Count);
        for (var i = 1; i < names.Count; i++)
        {
            Assert.NotEqual(names[i - 1], names[i]);
        }
    }

    [Fact]
    public void Animator_NoImmediateRepeat_WithThreeVariants()
    {
        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>>
        {
            [MascotState.Working] = [MakeClip("a"), MakeClip("b"), MakeClip("c")],
        };
        var (animator, time) = Create(Definition(clips));
        animator.SetState(MascotState.Working);

        string? previous = null;
        var seen = new HashSet<string>();
        for (var i = 0; i < 30; i++)
        {
            while (!animator.IsAnimating)
            {
                time.Advance(animator.UntilNextChange());
            }

            Assert.NotEqual(previous, animator.CurrentClip);
            previous = animator.CurrentClip;
            seen.Add(previous!);
            while (animator.IsAnimating)
            {
                time.Advance(animator.UntilNextChange());
            }
        }

        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Animator_SingleVariant_PlaysAgain()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Waiting);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("wave", animator.CurrentClip);
            time.Advance(TimeSpan.FromMilliseconds(300));
            Assert.False(animator.IsAnimating);
            time.Advance(animator.UntilNextChange());
        }
    }

    [Fact]
    public void Animator_Done_PlaysOnceThenIdle()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Working);
        animator.SetState(MascotState.Done);

        Assert.Equal(MascotState.Done, animator.State);
        Assert.Equal("cheer", animator.CurrentClip);
        time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(MascotState.Done, animator.State);
        Assert.True(animator.IsAnimating);

        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(MascotState.Idle, animator.State);
        Assert.False(animator.IsAnimating);
        Assert.InRange(animator.UntilNextChange(), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12));

        // Ensuite : des clips d'Idle, jamais « cheer » à nouveau.
        time.Advance(animator.UntilNextChange());
        Assert.True(animator.IsAnimating);
        Assert.Contains(animator.CurrentClip, new[] { "blink", "look" });
    }

    [Fact]
    public void Animator_Done_CaughtUpAfterLongGap()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Done);

        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(MascotState.Idle, animator.State);
    }

    [Fact]
    public void Animator_Done_WithoutClip_GoesStraightToIdle()
    {
        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>> { [MascotState.Idle] = [MakeClip("blink")] };
        var (animator, _) = Create(Definition(clips));
        animator.SetState(MascotState.Working);

        animator.SetState(MascotState.Done);

        Assert.Equal(MascotState.Idle, animator.State);
        Assert.False(animator.IsAnimating);
    }

    [Fact]
    public void Animator_Idle10Min_Sleeps()
    {
        var (animator, time) = Create();

        time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        Assert.False(animator.IsSleeping);
        Assert.True(animator.UntilNextChange() <= TimeSpan.FromMilliseconds(1));

        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(animator.IsSleeping);
        Assert.False(animator.IsAnimating);
        Assert.Null(animator.CurrentClip);
        Assert.Equal(TimeSpan.MaxValue, animator.UntilNextChange());

        // Toujours endormie bien plus tard, et SetState(Idle) ne la réveille pas.
        time.Advance(TimeSpan.FromHours(1));
        animator.SetState(MascotState.Idle);
        Assert.True(animator.IsSleeping);
        Assert.Equal(TimeSpan.MaxValue, animator.UntilNextChange());

        animator.SetState(MascotState.Working);

        Assert.False(animator.IsSleeping);
        Assert.True(animator.IsAnimating);
        Assert.StartsWith("type-", animator.CurrentClip);
    }

    [Fact]
    public void Animator_Idle10Min_SleepsEvenInOneBigJump()
    {
        var (animator, time) = Create();

        time.Advance(TimeSpan.FromMinutes(11));

        Assert.True(animator.IsSleeping);
    }

    [Fact]
    public void Animator_SleepCountdown_RestartsWhenReturningToIdle()
    {
        var (animator, time) = Create();
        time.Advance(TimeSpan.FromMinutes(9));
        animator.SetState(MascotState.Working);
        time.Advance(TimeSpan.FromMinutes(30));
        Assert.False(animator.IsSleeping);

        animator.SetState(MascotState.Idle);
        time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(animator.IsSleeping);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(animator.IsSleeping);
    }

    [Fact]
    public void Animator_Working_NeverSleeps()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Working);

        time.Advance(TimeSpan.FromHours(2));

        Assert.False(animator.IsSleeping);
    }

    [Fact]
    public void Animator_Waiting30Min_Sleeps_AndWakesOnStateChange()
    {
        var (animator, time) = Create();
        time.Advance(TimeSpan.FromMinutes(3));
        animator.SetState(MascotState.Waiting);

        time.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromMilliseconds(1));
        Assert.False(animator.IsSleeping);
        Assert.True(animator.UntilNextChange() <= TimeSpan.FromMilliseconds(1));

        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(animator.IsSleeping);
        Assert.False(animator.IsAnimating);
        Assert.Null(animator.CurrentClip);
        Assert.Equal(TimeSpan.MaxValue, animator.UntilNextChange());

        // SetState(Waiting) ne la réveille pas ; un autre état, si.
        time.Advance(TimeSpan.FromHours(1));
        animator.SetState(MascotState.Waiting);
        Assert.True(animator.IsSleeping);

        animator.SetState(MascotState.Working);
        Assert.False(animator.IsSleeping);
        Assert.True(animator.IsAnimating);
    }

    [Fact]
    public void Animator_Waiting_CountdownRestartsOnEachWait()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Waiting);
        time.Advance(TimeSpan.FromMinutes(29));
        animator.SetState(MascotState.Working);
        time.Advance(TimeSpan.FromMinutes(5));
        animator.SetState(MascotState.Waiting);

        time.Advance(TimeSpan.FromMinutes(29));
        Assert.False(animator.IsSleeping);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(animator.IsSleeping);
    }

    [Fact]
    public void Animator_IdleAfterDone_CountsFromEndOfDoneClip()
    {
        var (animator, time) = Create();
        animator.SetState(MascotState.Working);
        animator.SetState(MascotState.Done);
        // Le clip « cheer » dure 300 ms : les 10 min d'Idle commencent à sa fin.
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(animator.IsSleeping);

        time.Advance(TimeSpan.FromMilliseconds(300));

        Assert.True(animator.IsSleeping);
    }

    [Fact]
    public void Animator_StateWithoutClip_StaysAtRest()
    {
        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>> { [MascotState.Idle] = [MakeClip("blink")] };
        var (animator, time) = Create(Definition(clips));

        animator.SetState(MascotState.Working);

        Assert.Equal(MascotState.Working, animator.State);
        Assert.False(animator.IsAnimating);
        Assert.Null(animator.CurrentClip);
        Assert.Equal(TimeSpan.MaxValue, animator.UntilNextChange());
        time.Advance(TimeSpan.FromHours(1));
        Assert.False(animator.IsAnimating);
        Assert.False(animator.IsSleeping);
    }

    [Fact]
    public void Animator_IdleWithoutClips_StillSleeps()
    {
        var (animator, time) = Create(Definition(new Dictionary<MascotState, IReadOnlyList<Clip>>()));
        Assert.Equal(TimeSpan.FromMinutes(10), animator.UntilNextChange());

        time.Advance(TimeSpan.FromMinutes(10));

        Assert.True(animator.IsSleeping);
    }

    [Fact]
    public void Animator_SameSeed_SameSequence()
    {
        static List<string> Run()
        {
            var (animator, time) = Create(seed: 7);
            animator.SetState(MascotState.Working);
            var trace = new List<string>();
            for (var i = 0; i < 15; i++)
            {
                trace.Add($"{animator.CurrentClip}@{animator.UntilNextChange().TotalMilliseconds}");
                time.Advance(animator.UntilNextChange());
            }

            return trace;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Animator_Draw_ShowsRestSleepAndClipPoses()
    {
        var (animator, time) = Create();
        using var bitmap = new SKBitmap(8, 4);
        using var canvas = new SKCanvas(bitmap);
        var area = new SKRect(0, 0, 8, 4);

        SKColor Pixel(int x, int y)
        {
            canvas.Clear(SKColors.Transparent);
            animator.Draw(canvas, area);
            return bitmap.GetPixel(x, y);
        }

        // Scène 8×4 dans 8×4 : pixel de sprite = 1.
        Assert.Equal(RestColor, Pixel(0, 0));

        animator.SetState(MascotState.Working);
        Assert.Equal(ActColor, Pixel(1, 0));
        Assert.Equal(0, Pixel(0, 0).Alpha);
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(ActColor, Pixel(2, 0));
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(ActColor, Pixel(3, 0));

        animator.SetState(MascotState.Idle);
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(animator.IsSleeping);
        Assert.Equal(SleepColor, Pixel(0, 0));
    }

    [Fact]
    public void Animator_Draw_CentersSceneAtBottom()
    {
        // Scène 4×2 ; zone 100×20 : pixel de sprite = floor(min(25, 10)) = 10, scène de 40×20 centrée (x de 30 à 70).
        var definition = Definition(sceneWidth: 4, sceneHeight: 2) with { Rest = Still('r', 100, 0, 1) };
        var (animator, _) = Create(definition);
        using var bitmap = new SKBitmap(120, 30);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Zone décalée de (10, 5) : le bas de la zone est à y = 25.
        animator.Draw(canvas, new SKRect(10, 5, 110, 25));

        // Le pixel de sprite (0, 1) occupe x 40..50, y 15..25.
        Assert.Equal(RestColor, bitmap.GetPixel(40, 15));
        Assert.Equal(RestColor, bitmap.GetPixel(49, 24));
        Assert.Equal(0, bitmap.GetPixel(39, 20).Alpha);
        Assert.Equal(0, bitmap.GetPixel(50, 20).Alpha);
        Assert.Equal(0, bitmap.GetPixel(45, 14).Alpha);
        Assert.Equal(0, bitmap.GetPixel(45, 25).Alpha);
    }

    [Fact]
    public void Animator_Draw_PixelSizeIsWholeAndAtLeastOne()
    {
        // Scène 4×2 ; zone 39×19,9 : pixel = floor(min(9,75 ; 9,95)) = 9. Sprite en (0, 1) : 9×9.
        var definition = Definition(sceneWidth: 4, sceneHeight: 2) with { Rest = Still('r', 100, 0, 1) };
        var (animator, _) = Create(definition);
        using var bitmap = new SKBitmap(60, 30);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        animator.Draw(canvas, new SKRect(0, 0, 39, 19.9f));

        var painted = 0;
        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 60; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha != 0)
                {
                    painted++;
                }
            }
        }

        // Carré net de 9×9 (la zone n'étant pas entière, l'arrondi du bord reste dans la zone).
        Assert.Equal(81, painted);

        // Zone plus petite que la scène : le pixel de sprite ne descend jamais sous 1.
        canvas.Clear(SKColors.Transparent);
        animator.Draw(canvas, new SKRect(0, 0, 3, 1.5f));
        Assert.InRange(bitmap.GetPixel(0, 1).Alpha, 0, 255);
    }

    [Fact]
    public void Animator_Draw_StaysInsideArea()
    {
        // Calques qui sortent largement de la scène, pour que seul le découpage les retienne.
        var wild = new Clip("wild", [
            Moving(200, new SKPointI(-30, -30), new SKPointI(40, 30)),
            Moving(200, new SKPointI(40, 30), new SKPointI(-30, 20)),
        ]);
        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>>
        {
            [MascotState.Idle] = [wild],
            [MascotState.Working] = [wild, MakeClip("x")],
        };
        var definition = Definition(clips, sceneWidth: 20, sceneHeight: 10) with
        {
            Rest = new Pose(100, [new Layer(new Sprite(["rrrr", "rrrr"], Palette), new SKPointI(-3, 8), new SKPointI(-3, 8))]),
            Sleep = new Pose(100, [new Layer(new Sprite(["ssss", "ssss"], Palette), new SKPointI(19, 9), new SKPointI(19, 9))]),
        };

        var areas = new[]
        {
            new SKRect(40, 20, 140, 80),  // plus grande que la scène : pixel de 5, scène 100×50
            new SKRect(60, 30, 70, 35),   // plus petite que la scène : pixel de 1, scène débordante
            new SKRect(0, 0, 200, 100),   // tout le bitmap
        };

        foreach (var area in areas)
        {
            foreach (var state in new[] { MascotState.Idle, MascotState.Working })
            {
                var (animator, time) = Create(definition);
                animator.SetState(state == MascotState.Idle ? MascotState.Working : MascotState.Idle);
                animator.SetState(state);
                using var bitmap = new SKBitmap(200, 100);
                using var canvas = new SKCanvas(bitmap);
                var paintedInside = false;

                for (var step = 0; step < 400; step++)
                {
                    canvas.Clear(SKColors.Transparent);
                    animator.Draw(canvas, area);
                    for (var y = 0; y < 100; y++)
                    {
                        for (var x = 0; x < 200; x++)
                        {
                            var inside = x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom;
                            var alpha = bitmap.GetPixel(x, y).Alpha;
                            if (!inside)
                            {
                                Assert.True(alpha == 0, $"pixel peint hors zone en ({x}, {y}), zone {area}, étape {step}");
                            }
                            else if (alpha != 0)
                            {
                                paintedInside = true;
                            }
                        }
                    }

                    time.Advance(TimeSpan.FromMilliseconds(137));
                }

                Assert.True(paintedInside);
            }
        }
    }

    [Fact]
    public void Animator_Draw_SleepPoseStaysInsideAreaToo()
    {
        var definition = Definition(sceneWidth: 20, sceneHeight: 10) with
        {
            Sleep = new Pose(100, [new Layer(new Sprite(["ssss", "ssss"], Palette), new SKPointI(19, 9), new SKPointI(19, 9))]),
        };
        var (animator, time) = Create(definition);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(animator.IsSleeping);
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var area = new SKRect(40, 20, 140, 80);

        animator.Draw(canvas, area);

        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var inside = x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom;
                if (!inside)
                {
                    Assert.Equal(0, bitmap.GetPixel(x, y).Alpha);
                }
            }
        }
    }

    [Fact]
    public void Animator_Draw_InterpolatesOnWholePixels()
    {
        var clip = new Clip("slide", [Moving(1000, new SKPointI(0, 0), new SKPointI(4, 0))]);
        var clips = new Dictionary<MascotState, IReadOnlyList<Clip>> { [MascotState.Working] = [clip] };
        var (animator, time) = Create(Definition(clips, sceneWidth: 8, sceneHeight: 1));
        animator.SetState(MascotState.Working);
        using var bitmap = new SKBitmap(8, 1);
        using var canvas = new SKCanvas(bitmap);

        // Linéaire de 0 à 4 en 1 s : à 380 ms, 1,52 arrondi à 2 ; à 120 ms, 0,48 arrondi à 0.
        time.Advance(TimeSpan.FromMilliseconds(120));
        canvas.Clear(SKColors.Transparent);
        animator.Draw(canvas, new SKRect(0, 0, 8, 1));
        Assert.Equal(ActColor, bitmap.GetPixel(0, 0));

        time.Advance(TimeSpan.FromMilliseconds(260));
        canvas.Clear(SKColors.Transparent);
        animator.Draw(canvas, new SKRect(0, 0, 8, 1));
        Assert.Equal(ActColor, bitmap.GetPixel(2, 0));
        Assert.Equal(0, bitmap.GetPixel(1, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(3, 0).Alpha);
    }

    [Fact]
    public void Layer_PositionIsWholePixels()
    {
        var layer = new Layer(One('a'), new SKPointI(0, 0), new SKPointI(1, -1));

        Assert.Equal(new SKPointI(0, 0), layer.PositionAt(Ease.Linear, 0f));
        Assert.Equal(new SKPointI(0, 0), layer.PositionAt(Ease.Linear, 0.49f));
        // 0,5 s'éloigne de zéro, dans les deux sens.
        Assert.Equal(new SKPointI(1, -1), layer.PositionAt(Ease.Linear, 0.5f));
        Assert.Equal(new SKPointI(1, -1), layer.PositionAt(Ease.Linear, 1f));

        var far = new Layer(One('a'), new SKPointI(10, 4), new SKPointI(-10, 4));
        Assert.Equal(new SKPointI(0, 4), far.PositionAt(Ease.Linear, 0.5f));
        Assert.Equal(new SKPointI(-10, 4), far.PositionAt(Ease.Linear, 9f));

        // OutBack dépasse l'arrivée avant d'y revenir : 10 × 1,0875 arrondi à 11.
        var back = new Layer(One('a'), new SKPointI(0, 0), new SKPointI(10, 0));
        Assert.Equal(11, back.PositionAt(Ease.OutBack, 0.5f).X);
        Assert.Equal(10, back.PositionAt(Ease.OutBack, 1f).X);
    }

    [Fact]
    public void Clip_Duration_SumsPoses()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(300), MakeClip("c").Duration);
        Assert.Equal(TimeSpan.Zero, new Clip("vide", []).Duration);
    }

    [Fact]
    public void Animator_InvalidScene_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MascotAnimator(Definition(sceneWidth: 0), new FakeTimeProvider(), new Random(1)));
    }

    [Fact]
    public async Task Animator_UsedFromTwoThreads_StaysConsistent()
    {
        var (animator, time) = Create();
        using var bitmap = new SKBitmap(64, 32);
        using var canvas = new SKCanvas(bitmap);
        var stop = false;
        var setter = Task.Run(() =>
        {
            var states = Enum.GetValues<MascotState>();
            for (var i = 0; !Volatile.Read(ref stop); i++)
            {
                animator.SetState(states[i % states.Length]);
                time.Advance(TimeSpan.FromMilliseconds(50));
            }
        });

        for (var i = 0; i < 2000; i++)
        {
            _ = animator.IsAnimating;
            _ = animator.UntilNextChange();
            animator.Draw(canvas, new SKRect(0, 0, 64, 32));
        }

        Volatile.Write(ref stop, true);
        await setter;
    }
}
