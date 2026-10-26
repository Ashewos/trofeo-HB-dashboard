using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public sealed class PanelAlphaSmootherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly SKRect Rect = SKRect.Create(10, 20, 400, 200);

    [Fact]
    public void FirstCall_TakesTheTargetDirectly()
    {
        var smoother = new PanelAlphaSmoother();

        Assert.Equal(0.7f, smoother.Smooth(Rect, 0.7f, T0));
    }

    [Fact]
    public void Rising_FollowsWithAFastTimeConstant()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.1f, T0);

        // Une constante de temps (0,4 s) plus tard : 63 % du chemin parcouru.
        float alpha = smoother.Smooth(Rect, 0.9f, T0 + PanelAlphaSmoother.RiseTime);

        Assert.Equal(0.9f - 0.8f * MathF.Exp(-1f), alpha, 3);
    }

    [Fact]
    public void Falling_FollowsWithASlowTimeConstant()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.9f, T0);

        // 1 s plus tard (le plus grand pas permis) : seulement 18 % du chemin parcouru vers le bas.
        float alpha = smoother.Smooth(Rect, 0.1f, T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(0.1f + 0.8f * MathF.Exp(-1f / 5f), alpha, 3);
        Assert.True(alpha > 0.7f);
    }

    [Fact]
    public void Falling_ReachesTheTargetAfterAboutFifteenSeconds()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.9f, T0);

        float alpha = 0.9f;
        for (int i = 1; i <= 15 * 12; i++) alpha = smoother.Smooth(Rect, 0.1f, T0 + TimeSpan.FromMilliseconds(i * 1000.0 / 12));

        Assert.InRange(alpha, 0.1f, 0.1f + 0.8f * MathF.Exp(-3f) + 0.01f); // 15 s = trois constantes de temps
    }

    [Fact]
    public void NoVisibleJump_BetweenTwoFramesAt12Fps()
    {
        var smoother = new PanelAlphaSmoother();
        float previous = smoother.Smooth(Rect, 0.1f, T0);

        float biggest = 0f;
        for (int i = 1; i <= 120; i++)
        {
            float target = i % 2 == 0 ? 0.1f : 0.9f; // le pire cas : une cible qui saute à chaque image
            float alpha = smoother.Smooth(Rect, target, T0 + TimeSpan.FromMilliseconds(i * 1000.0 / 12));
            biggest = Math.Max(biggest, Math.Abs(alpha - previous));
            previous = alpha;
        }

        // Une montée ne rattrape jamais plus de 19 % de l'écart en une image (1 − e^(−83 ms / 0,4 s)), une descente 1,6 %.
        float oneFrame = 0.8f * (1f - MathF.Exp(-(1f / 12f) / (float)PanelAlphaSmoother.RiseTime.TotalSeconds));
        Assert.True(biggest <= oneFrame + 1e-4f, $"plus grand saut : {biggest} (permis : {oneFrame})");
        Assert.True(biggest < 0.16f);
    }

    [Fact]
    public void LongGap_IsBoundedToOneSecond()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.1f, T0);

        // Une heure de trou (veille, horloge réglée) : pas de bond jusqu'à la cible.
        float alpha = smoother.Smooth(Rect, 0.9f, T0 + TimeSpan.FromHours(1));

        Assert.Equal(0.9f - 0.8f * MathF.Exp(-1f / 0.4f), alpha, 3);
        Assert.True(alpha < 0.9f);
    }

    [Fact]
    public void ClockGoingBackwards_ChangesNothing_ThenResumes()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.1f, T0);
        smoother.Smooth(Rect, 0.9f, T0 + TimeSpan.FromMilliseconds(100));

        float same = smoother.Smooth(Rect, 0.9f, T0 - TimeSpan.FromHours(1));
        float later = smoother.Smooth(Rect, 0.9f, T0 - TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(100));

        Assert.True(same < 0.9f);
        Assert.True(later > same); // l'horloge repart de sa nouvelle valeur, la montée continue
    }

    [Fact]
    public void SameInstant_ChangesNothing()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.2f, T0);
        float a = smoother.Smooth(Rect, 0.8f, T0 + TimeSpan.FromMilliseconds(80));

        // Le panneau d'erreur repeint le même panneau à la même heure : même alpha.
        Assert.Equal(a, smoother.Smooth(Rect, 0.8f, T0 + TimeSpan.FromMilliseconds(80)));
    }

    [Fact]
    public void EachRectangleKeepsItsOwnState()
    {
        var smoother = new PanelAlphaSmoother();
        var other = SKRect.Create(500, 20, 400, 200);
        smoother.Smooth(Rect, 0.1f, T0);
        smoother.Smooth(other, 0.8f, T0);

        float a = smoother.Smooth(Rect, 0.1f, T0 + TimeSpan.FromMilliseconds(200));
        float b = smoother.Smooth(other, 0.8f, T0 + TimeSpan.FromMilliseconds(200));

        Assert.Equal(0.1f, a, 4);
        Assert.Equal(0.8f, b, 4);
    }

    [Fact]
    public void TargetEqualToCurrent_IsStable()
    {
        var smoother = new PanelAlphaSmoother();
        smoother.Smooth(Rect, 0.35f, T0);

        for (int i = 1; i < 50; i++)
        {
            Assert.Equal(0.35f, smoother.Smooth(Rect, 0.35f, T0 + TimeSpan.FromMilliseconds(83 * i)), 6);
        }
    }
}
