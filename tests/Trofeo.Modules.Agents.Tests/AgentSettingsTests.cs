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

    [Fact]
    public void Settings_Parse_FeedDefaults_NoFadeNewestOnTop()
    {
        var settings = Parse("{}");

        Assert.False(settings.FeedFade);
        Assert.Equal(FeedNewest.Top, settings.FeedNewest);
    }

    [Fact]
    public void Settings_Parse_FeedFade()
    {
        Assert.True(Parse("""{"feedFade":true}""").FeedFade);
        Assert.False(Parse("""{"feedFade":false}""").FeedFade);
    }

    [Theory]
    [InlineData("""{"feedNewest":"bottom"}""")]
    [InlineData("""{"feedNewest":" Bottom "}""")]
    [InlineData("""{"feedNewest":"BOTTOM"}""")]
    public void Settings_Parse_FeedNewestBottom(string json)
    {
        Assert.Equal(FeedNewest.Bottom, Parse(json).FeedNewest);
    }

    [Theory]
    [InlineData("""{"feedNewest":"top"}""")]
    [InlineData("""{"feedNewest":" TOP "}""")]
    [InlineData("""{"feedNewest":"middle"}""")]
    [InlineData("""{"feedNewest":3}""")]
    [InlineData("""{"feedNewest":null}""")]
    public void Settings_Parse_FeedNewestTopOrUnknown_GivesTop(string json)
    {
        Assert.Equal(FeedNewest.Top, Parse(json).FeedNewest);
    }

    [Theory]
    [InlineData("""{"feedFade":"yes"}""")]
    [InlineData("""{"feedFade":1}""")]
    [InlineData("""{"feedFade":null}""")]
    public void Settings_Parse_InvalidFeedFade_GivesDefault(string json)
    {
        Assert.False(Parse(json).FeedFade);
    }

    [Fact]
    public void Settings_Parse_FeedOptionsKeepTheOtherSettings()
    {
        var settings = Parse("""{"view":"overview","autoFollow":false,"feedFade":true,"feedNewest":"bottom"}""");

        Assert.Equal(new AgentSettings(AgentView.Overview, false, true, FeedNewest.Bottom), settings);
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
