using System.Text.Json;

namespace Trofeo.Modules.Agents.Tests;

public sealed class AgentSettingsTests
{
    private static AgentSettings Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AgentSettings.Parse(document.RootElement);
    }

    [Fact]
    public void Settings_Parse()
    {
        Assert.Equal(new AgentSettings(AgentView.Feed, true), Parse("{}"));
        Assert.Equal(new AgentSettings(AgentView.Overview, true), Parse("""{"view":"overview"}"""));
        Assert.Equal(new AgentSettings(AgentView.Feed, true), Parse("""{"view":"feed"}"""));
        Assert.Equal(new AgentSettings(AgentView.Overview, false), Parse("""{"view":"Overview","autoFollow":false}"""));
        Assert.Equal(new AgentSettings(AgentView.Feed, false), Parse("""{"autoFollow":false}"""));
    }

    [Theory]
    [InlineData("""{"view":"cube"}""")]
    [InlineData("""{"view":3}""")]
    [InlineData("""{"view":null,"autoFollow":"oui"}""")]
    [InlineData("""{"autoFollow":1}""")]
    [InlineData("[]")]
    [InlineData("\"feed\"")]
    [InlineData("null")]
    public void Settings_Parse_UnknownValuesGiveDefaults(string json)
    {
        Assert.Equal(new AgentSettings(), Parse(json));
    }

    [Fact]
    public void Settings_Parse_DefaultElement_GivesDefaults()
    {
        Assert.Equal(new AgentSettings(), AgentSettings.Parse(default));
    }
}
