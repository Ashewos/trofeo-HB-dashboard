using SkiaSharp;

namespace Trofeo.Modules.Agents.Tests;

public sealed class MascotDefinitionTests
{
    private static readonly MascotState[] AllStates = [MascotState.Idle, MascotState.Working, MascotState.Waiting, MascotState.Done];

    // Le personnage (16 × 10) est posé en bas de la scène (24 × 16) et centré : colonnes 4 à 19, rangées 6 à 15.
    private static readonly SKRectI Body = new(4, 6, 20, 16);

    public static TheoryData<string> MascotNames => ["Claude", "Codex"];

    private static MascotDefinition Get(string name) => name switch
    {
        "Claude" => ClaudeMascot.Definition,
        "Codex" => CodexMascot.Definition,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static IEnumerable<Pose> AllPoses(MascotDefinition definition) =>
        new[] { definition.Rest, definition.Sleep }.Concat(definition.Clips.Values.SelectMany(clips => clips).SelectMany(clip => clip.Poses));

    private static SKRectI Box(Sprite sprite, SKPointI at) => new(at.X, at.Y, at.X + sprite.Width, at.Y + sprite.Height);

    private static bool Inside(SKRectI inner, SKRectI outer) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void Scene_Is24By16(string name)
    {
        var definition = Get(name);
        Assert.Equal(24, definition.SceneWidth);
        Assert.Equal(16, definition.SceneHeight);
    }

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void EveryPose_AtLeast83ms(string name)
    {
        var definition = Get(name);
        foreach (var pose in AllPoses(definition))
        {
            Assert.True(pose.DurationMs >= 83, $"{name} : pose de {pose.DurationMs} ms.");
        }
    }

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void EachState_HasTwoVariants_ExceptDone(string name)
    {
        var definition = Get(name);
        foreach (var state in AllStates)
        {
            Assert.True(definition.Clips.TryGetValue(state, out var clips), $"{name} : aucun clip pour {state}.");
            Assert.True(clips!.Count >= (state == MascotState.Done ? 1 : 2), $"{name} : {clips.Count} clip(s) pour {state}.");
            Assert.All(clips, clip => Assert.NotEmpty(clip.Poses));
        }
    }

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void Layers_StayInsideScene(string name)
    {
        var definition = Get(name);
        var scene = new SKRectI(0, 0, definition.SceneWidth, definition.SceneHeight);
        foreach (var pose in AllPoses(definition))
        {
            foreach (var layer in pose.Layers)
            {
                Assert.True(Inside(Box(layer.Sprite, layer.From), scene), $"{name} : calque sorti de la scène au départ ({layer.From}).");
                Assert.True(Inside(Box(layer.Sprite, layer.To), scene), $"{name} : calque sorti de la scène à l'arrivée ({layer.To}).");
            }
        }
    }

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void ClipNames_UniquePerState(string name)
    {
        var definition = Get(name);
        foreach (var (state, clips) in definition.Clips)
        {
            Assert.Equal(clips.Count, clips.Select(clip => clip.Name).Distinct().Count());
            Assert.All(clips, clip => Assert.False(string.IsNullOrWhiteSpace(clip.Name), $"{name} : clip sans nom pour {state}."));
        }
    }

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void Rest_HasNoPropsOutsideBody(string name)
    {
        var definition = Get(name);
        Assert.NotEmpty(definition.Rest.Layers);
        foreach (var layer in definition.Rest.Layers)
        {
            Assert.Equal(layer.From, layer.To);
            Assert.True(Inside(Box(layer.Sprite, layer.From), Body), $"{name} : calque de repos hors du corps ({layer.From}).");
        }
    }

    [Theory]
    [MemberData(nameof(MascotNames))]
    public void Sleep_IsAStillImage(string name)
    {
        var definition = Get(name);
        Assert.NotEmpty(definition.Sleep.Layers);
        Assert.All(definition.Sleep.Layers, layer => Assert.Equal(layer.From, layer.To));
    }

    [Fact]
    public void Claude_ClipNames()
    {
        var clips = ClaudeMascot.Definition.Clips;
        Assert.Equal(["balade", "regarde"], clips[MascotState.Idle].Select(c => c.Name));
        Assert.Equal(["haltère", "développé"], clips[MascotState.Working].Select(c => c.Name));
        Assert.Equal(["drapeau", "saut-drapeau"], clips[MascotState.Waiting].Select(c => c.Name));
        Assert.Equal(["confettis"], clips[MascotState.Done].Select(c => c.Name));
    }

    [Fact]
    public void Codex_ClipNames()
    {
        var clips = CodexMascot.Definition.Clips;
        Assert.Equal(["dort", "fredonne"], clips[MascotState.Idle].Select(c => c.Name));
        Assert.Equal(["tape", "tape-vite"], clips[MascotState.Working].Select(c => c.Name));
        Assert.Equal(["question", "exclamation"], clips[MascotState.Waiting].Select(c => c.Name));
        Assert.Equal(["cœurs"], clips[MascotState.Done].Select(c => c.Name));
    }

    [Fact]
    public void Claude_Rest_IsTheDrawnPose()
    {
        // La pose de repos du brief : le corps de 16 × 10 à sa place, et les yeux dans le corps.
        var layers = ClaudeMascot.Definition.Rest.Layers;
        Assert.Contains(layers, layer => layer.Sprite.Width == 16 && layer.Sprite.Height == 10 && layer.From == new SKPointI(4, 6));
    }

    [Fact]
    public void Claude_ClipDurations_FollowTheBrief()
    {
        var idle = ClaudeMascot.Definition.Clips[MascotState.Idle];
        var walk = idle.Single(c => c.Name == "balade");
        Assert.Equal(8, walk.Poses.Count);
        Assert.All(walk.Poses, pose => Assert.Equal(110, pose.DurationMs));

        var look = idle.Single(c => c.Name == "regarde");
        Assert.Equal(400, look.Poses[0].DurationMs);
        Assert.Equal(400, look.Poses[^2].DurationMs);
        Assert.Equal(120, look.Poses[^1].DurationMs);

        var working = ClaudeMascot.Definition.Clips[MascotState.Working];
        Assert.Equal(12, working.Single(c => c.Name == "haltère").Poses.Count);
        Assert.Equal(6, working.Single(c => c.Name == "développé").Poses.Count);

        Assert.Equal(10, ClaudeMascot.Definition.Clips[MascotState.Done].Single().Poses.Count);
    }

    [Fact]
    public void Codex_ClipDurations_FollowTheBrief()
    {
        var clips = CodexMascot.Definition.Clips;
        Assert.Equal(6, clips[MascotState.Idle].Single(c => c.Name == "dort").Poses.Count);
        Assert.Equal(8, clips[MascotState.Idle].Single(c => c.Name == "fredonne").Poses.Count);
        Assert.Equal(6, clips[MascotState.Working].Single(c => c.Name == "tape").Poses.Count);
        Assert.Equal(10, clips[MascotState.Working].Single(c => c.Name == "tape-vite").Poses.Count);
        Assert.All(clips[MascotState.Working].Single(c => c.Name == "tape-vite").Poses, pose => Assert.Equal(85, pose.DurationMs));
        Assert.Equal(10, clips[MascotState.Done].Single().Poses.Count);
    }

    [Fact]
    public void Burst_SameSeed_RepeatsIdentically()
    {
        Sprite[] dots = [Particles.Dot(SKColors.Red), Particles.Dot(SKColors.Blue)];
        var area = new SKRectI(0, 0, 24, 6);
        for (var step = 0; step < 10; step++)
        {
            var first = Particles.Burst(dots, 12, 7, new SKPointI(12, 6), area, step, 10, Ease.OutQuad);
            var second = Particles.Burst(dots, 12, 7, new SKPointI(12, 6), area, step, 10, Ease.OutQuad);
            Assert.Equal(first.Select(l => (l.From, l.To)), second.Select(l => (l.From, l.To)));
        }
    }

    [Fact]
    public void Burst_DifferentSeeds_Differ()
    {
        Sprite[] dots = [Particles.Dot(SKColors.Red)];
        var area = new SKRectI(0, 0, 24, 6);
        var a = Particles.Burst(dots, 12, 1, new SKPointI(12, 6), area, 9, 10, Ease.OutQuad);
        var b = Particles.Burst(dots, 12, 2, new SKPointI(12, 6), area, 9, 10, Ease.OutQuad);
        Assert.NotEqual(a.Select(l => l.To), b.Select(l => l.To));
    }

    [Fact]
    public void Burst_FollowsTheEasingFromOriginToTarget()
    {
        Sprite[] dots = [Particles.Dot(SKColors.Red)];
        var area = new SKRectI(0, 0, 24, 6);
        var origin = new SKPointI(12, 6);
        var first = Particles.Burst(dots, 24, 3, origin, area, 0, 10, Ease.OutQuad);
        Assert.NotEmpty(first);
        Assert.All(first, layer => Assert.Equal(origin, layer.From));

        // Chaque pas reprend où le précédent s'est arrêté : le mouvement est continu d'une pose à l'autre.
        var previous = Particles.Burst(dots, 24, 3, origin, area, 4, 10, Ease.OutQuad);
        var next = Particles.Burst(dots, 24, 3, origin, area, 5, 10, Ease.OutQuad);
        Assert.All(next, layer => Assert.Contains(previous, other => other.To == layer.From));
    }

    [Fact]
    public void Burst_KeepsEverySpriteInsideTheArea()
    {
        var heart = new Sprite(["h.h", "hhh", ".h."], new Dictionary<char, SKColor> { ['h'] = SKColors.HotPink });
        var area = new SKRectI(0, 0, 24, 6);
        for (var step = 0; step < 10; step++)
        {
            foreach (var layer in Particles.Burst([heart], 6, 11, new SKPointI(10, 4), area, step, 10, Ease.OutQuad))
            {
                Assert.True(Inside(Box(heart, layer.To), new SKRectI(0, 0, 24, 8)));
            }
        }
    }
}
