using System.Text.Json;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Network.Tests;

public class NetworkFactoryTests
{
    private static IConfigurableModuleFactory Factory() =>
        new NetworkFactory((_, _) => throw new InvalidOperationException("Aucune source attendue."));

    private static JsonElement Json(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Network_Schema_HasSourceAndUniFiFields()
    {
        var fields = Factory().Schema.Fields;

        Assert.Equal(["source", "unifi.host", "unifi.apiKey", "forget-certificate"], fields.Select(f => f.Key));

        var source = Assert.IsType<ChoiceField>(fields[0]);
        Assert.Equal("Source du débit", source.Label);
        Assert.Equal(
            [new SettingOption("pc", "Ce PC"), new SettingOption("freebox", "Freebox"), new SettingOption("unifi", "UniFi")],
            source.Options);
        Assert.Equal("pc", source.Default);

        var host = Assert.IsType<TextField>(fields[1]);
        Assert.Equal("Adresse de la console", host.Label);
        Assert.Equal("192.168.1.1", host.Placeholder);

        var key = Assert.IsType<SecretField>(fields[2]);
        Assert.Equal("Clé API de la console", key.Label);

        var forget = Assert.IsType<ActionField>(fields[3]);
        Assert.Equal("Oublier le certificat de la console", forget.Label);
        Assert.Equal(
            "La prochaine connexion fera confiance au certificat présenté. Ne le fais qu'après un renouvellement que tu connais.",
            forget.Confirm);
    }

    [Fact]
    public void Network_UniFiFields_VisibleOnlyForUniFi()
    {
        var factory = Factory();
        var schema = factory.Schema;
        var unifiFields = schema.Fields.Skip(1).ToArray();

        Assert.All(unifiFields, f => Assert.Equal(new FieldCondition("source", "unifi"), f.VisibleWhen));
        Assert.Null(schema.Fields[0].VisibleWhen);

        var pc = Json("""{"source":"pc"}""");
        var unifi = Json("""{"source":"unifi"}""");
        var unset = Json("{}");
        Assert.All(unifiFields, f => Assert.False(SettingsDocument.IsVisible(f, pc, schema)));
        Assert.All(unifiFields, f => Assert.False(SettingsDocument.IsVisible(f, unset, schema)));
        Assert.All(unifiFields, f => Assert.True(SettingsDocument.IsVisible(f, unifi, schema)));
    }

    [Fact]
    public async Task Network_ForgetCertificate_ClearsPinForLowercaseHost()
    {
        var secrets = new FakeSecretStore();
        secrets.Set("unifi.certSha256.console.local", "AA");
        secrets.Set("unifi.certSha256.autre", "BB");

        var result = await Factory().RunActionAsync(
            "forget-certificate", Json("""{"source":"unifi","unifi":{"host":"Console.Local"}}"""), secrets, CancellationToken.None);

        Assert.Equal(new ActionResult(true, "Certificat oublié : la prochaine connexion mémorisera le nouveau."), result);
        Assert.Null(secrets.Get("unifi.certSha256.console.local"));
        Assert.Equal("BB", secrets.Get("unifi.certSha256.autre"));
    }

    [Fact]
    public async Task Network_ForgetCertificate_WithoutHost_Refuses()
    {
        var secrets = new FakeSecretStore();
        secrets.Set("unifi.certSha256.console.local", "AA");

        var result = await Factory().RunActionAsync("forget-certificate", Json("{}"), secrets, CancellationToken.None);

        Assert.Equal(new ActionResult(false, "Indique d'abord l'adresse de la console."), result);
        Assert.Equal("AA", secrets.Get("unifi.certSha256.console.local"));
    }

    [Fact]
    public async Task Network_UnknownAction_IsRefused()
    {
        var result = await Factory().RunActionAsync("zzz", Json("{}"), new FakeSecretStore(), CancellationToken.None);

        Assert.False(result.Ok);
    }
}
