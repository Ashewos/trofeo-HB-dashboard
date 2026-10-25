using System.Text.Json;

namespace Trofeo.Modules.Network.Tests;

public class NetworkSettingsTests
{
    private static NetworkSettings Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return NetworkSettings.FromJson(doc.RootElement);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"source":"inconnue"}""")]
    [InlineData("""{"source":3}""")]
    [InlineData("""{"source":null}""")]
    public void Settings_DefaultIsPc(string json) => Assert.Equal(new NetworkSettings(), Parse(json));

    [Theory]
    [InlineData("pc", NetworkSourceKind.Pc)]
    [InlineData("freebox", NetworkSourceKind.Freebox)]
    [InlineData("unifi", NetworkSourceKind.UniFi)]
    [InlineData("UniFi", NetworkSourceKind.UniFi)]
    [InlineData("FREEBOX", NetworkSourceKind.Freebox)]
    public void Settings_SourceNames_AreCaseInsensitive(string name, NetworkSourceKind expected) =>
        Assert.Equal(expected, Parse($$"""{"source":"{{name}}"}""").Source);

    [Fact]
    public void Settings_UniFiHost_IsRead()
    {
        var settings = Parse("""{"source":"unifi","unifi":{"host":" 192.168.1.1 "}}""");

        Assert.Equal(NetworkSourceKind.UniFi, settings.Source);
        Assert.Equal("192.168.1.1", settings.UniFiHost);
    }

    [Theory]
    [InlineData("""{"source":"unifi"}""")]
    [InlineData("""{"source":"unifi","unifi":{}}""")]
    [InlineData("""{"source":"unifi","unifi":{"host":""}}""")]
    [InlineData("""{"source":"unifi","unifi":{"host":7}}""")]
    [InlineData("""{"source":"unifi","unifi":"x"}""")]
    public void Settings_UniFiHost_MissingOrInvalidIsNull(string json) => Assert.Null(Parse(json).UniFiHost);
}
