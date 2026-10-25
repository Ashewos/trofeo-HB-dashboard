namespace Trofeo.Modules.Agents.Tests;

public sealed class EasingTests
{
    public static TheoryData<Ease> AllEases => new(Enum.GetValues<Ease>());

    [Theory]
    [MemberData(nameof(AllEases))]
    public void Easing_Endpoints(Ease ease)
    {
        Assert.Equal(0f, Easing.Apply(ease, 0f));
        Assert.Equal(1f, Easing.Apply(ease, 1f));
    }

    [Theory]
    [MemberData(nameof(AllEases))]
    public void Easing_ClampsOutOfRange(Ease ease)
    {
        Assert.Equal(0f, Easing.Apply(ease, -3f));
        Assert.Equal(1f, Easing.Apply(ease, 7f));
        Assert.Equal(0f, Easing.Apply(ease, float.NaN));
    }

    [Fact]
    public void Easing_OutBack_OvershootsOne()
    {
        Assert.True(Easing.Apply(Ease.OutBack, 0.5f) > 1f);
    }

    [Fact]
    public void Easing_Midpoints()
    {
        Assert.Equal(0.5f, Easing.Apply(Ease.Linear, 0.5f), 5);
        Assert.Equal(0.25f, Easing.Apply(Ease.InQuad, 0.5f), 5);
        Assert.Equal(0.75f, Easing.Apply(Ease.OutQuad, 0.5f), 5);
        Assert.Equal(0.5f, Easing.Apply(Ease.InOutQuad, 0.5f), 5);
        Assert.Equal(0.125f, Easing.Apply(Ease.InOutQuad, 0.25f), 5);
    }
}
