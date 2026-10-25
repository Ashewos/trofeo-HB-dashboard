namespace Trofeo.Modules.Agents.Tests;

public class ModelNamesTests
{
    [Theory]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
    [InlineData("claude-haiku-5-5[1m]", "Haiku 5.5")]
    [InlineData("claude-fable-5-1", "Fable 5.1")]
    [InlineData("gpt-5.1-codex-max", "GPT-5.1 Codex Max")]
    [InlineData("<synthetic>", "—")]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("modele-inconnu", "modele-inconnu")]
    public void Display_Models(string? id, string expected) => Assert.Equal(expected, ModelNames.Display(id));
}
