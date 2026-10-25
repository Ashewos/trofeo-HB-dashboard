using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents.Tests;

public sealed class RemainingPercentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed class NoSecrets : ISecretStore
    {
        public string? Get(string key) => null;
        public void Set(string key, string? value) { }
    }

    private static AgentModule Create(bool codex, double? context, QuotaWindow? fiveHour, QuotaWindow? weekly)
    {
        var source = new FakeAgentSource();
        source.Enqueue(new AgentSnapshot(true,
            [new AgentSessionSummary("a", "projet", AgentActivity.Idle, Now.AddHours(-1), Now)],
            new AgentSessionDetail("a", null, null, [], [], null, context),
            new AgentQuotas(fiveHour, weekly, Now)));
        IModuleFactory factory = codex ? new CodexFactory(_ => source) : new ClaudeCodeFactory(_ => source);
        using var settings = JsonDocument.Parse("{}");
        var module = (AgentModule)factory.Create(new ModuleContext(Guid.NewGuid(), settings.RootElement.Clone(),
            new FakeTimeProvider(Now), new FakeModuleLog(), new NoSecrets()));
        module.PollNow();
        module.PollNow();
        return module;
    }

    private static SKBitmap Draw(AgentModule module, out List<string> texts)
    {
        texts = [];
        module.Painter.DrawnTexts = texts;
        var bitmap = new SKBitmap(960, 462);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Themes.Classique.Background);
        module.Render(canvas, new ModuleRenderContext(new SKRect(0, 0, 960, 462), new GridSize(8, 4),
            Themes.Classique, 1f, Now));
        module.Painter.DrawnTexts = null;
        return bitmap;
    }

    [Theory]
    [InlineData(true, "30 %", "80 %", "60 %")]
    [InlineData(false, "30 %", "20 %", "40 %")]
    public void Factories_ContextStaysUsedAndOnlyCodexQuotasAreRemaining(bool codex, string context, string fiveHour, string weekly)
    {
        using var module = Create(codex, 30, new QuotaWindow(20, Now.AddHours(1)), new QuotaWindow(40, Now.AddDays(1)));
        using var bitmap = Draw(module, out var texts);

        Assert.Contains(context, texts);
        Assert.Contains(fiveHour, texts);
        Assert.Contains(weekly, texts);
        Assert.Equal(3, texts.Count(t => t.EndsWith(" %", StringComparison.Ordinal)));
        Assert.Equal(2, texts.Count(t => t.StartsWith("reset ", StringComparison.Ordinal)));

        // Au format 960 × 462, les jauges mesurent 170 px et démarrent à x=592 et x=774.
        // Le centre doit être rempli pour Codex (80/60 %), vide pour Claude (20/40 %).
        var accent = Themes.Classique.Accent;
        Assert.Equal(codex, bitmap.GetPixel(677, 413) == accent);
        Assert.Equal(codex, bitmap.GetPixel(859, 413) == accent);
    }

    [Theory]
    [InlineData(0, "100 %")]
    [InlineData(100, "0 %")]
    [InlineData(30, "70 %")]
    [InlineData(-5, "100 %")]
    [InlineData(105, "0 %")]
    [InlineData(77.5, "23 %")]
    public void Codex_ConvertsOnlyQuotasAndKeepsUsedContext(double used, string expected)
    {
        using var module = Create(true, used, new QuotaWindow(used, null), new QuotaWindow(used, null));
        using var bitmap = Draw(module, out var texts);

        Assert.Equal(2, texts.Count(t => t == expected));
        Assert.Contains(AgentText.Percent(used), texts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Codex_UnknownContextOrQuotaNeverBecomesFull(bool nan)
    {
        using var module = Create(true, nan ? double.NaN : null,
            nan ? new QuotaWindow(double.NaN, null) : null, new QuotaWindow(20, null));
        using var bitmap = Draw(module, out var texts);

        Assert.Equal(4, texts.Count(t => t == "—")); // contexte, quota, durée et modèle
        Assert.DoesNotContain("100 %", texts);
        Assert.Contains("80 %", texts);
        Assert.NotEqual(Themes.Classique.Accent, bitmap.GetPixel(677, 413));
    }

    [Fact]
    public void Codex_ExpiredQuotaRemainsUnknownAndEmpty()
    {
        using var module = Create(true, null, new QuotaWindow(20, Now.AddMinutes(-1)), new QuotaWindow(40, Now.AddDays(1)));
        using var bitmap = Draw(module, out var texts);

        Assert.Contains("reset passé", texts);
        Assert.DoesNotContain("80 %", texts);
        Assert.Contains("60 %", texts);
        Assert.NotEqual(Themes.Classique.Accent, bitmap.GetPixel(677, 413));
    }
}
