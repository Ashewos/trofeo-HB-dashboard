using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>
/// Fabrique du module Réseau. Les réglages sont lus ici ; <paramref name="createSource"/> choisit la source de débit
/// correspondante.
/// </summary>
public sealed class NetworkFactory(Func<NetworkSettings, ModuleContext, IThroughputSource> createSource) : IConfigurableModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "network",
        "Réseau",
        new GridSize(3, 1),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(3, 1),
            [SizePreset.Ideal] = new GridSize(4, 2),
            [SizePreset.Large] = new GridSize(6, 2),
        });

    private const string ForgetCertificate = "forget-certificate";

    private static readonly FieldCondition IfUniFi = new("source", "unifi");

    public SettingsSchema Schema { get; } = new(
    [
        new ChoiceField("source", "Source du débit",
            [new SettingOption("pc", "Ce PC"), new SettingOption("freebox", "Freebox"), new SettingOption("unifi", "UniFi")], "pc"),
        new TextField("unifi.host", "Adresse de la console") { Placeholder = "192.168.1.1", VisibleWhen = IfUniFi },
        new SecretField(UniFiSource.KeyName, "Clé API de la console") { VisibleWhen = IfUniFi },
        new ActionField(ForgetCertificate, "Oublier le certificat de la console")
        {
            Confirm = "La prochaine connexion fera confiance au certificat présenté. Ne le fais qu'après un renouvellement que tu connais.",
            VisibleWhen = IfUniFi,
        },
    ]);

    public Task<ActionResult> RunActionAsync(string actionKey, JsonElement settings, ISecretStore secrets, CancellationToken ct)
    {
        if (actionKey != ForgetCertificate) return Task.FromResult(new ActionResult(false, "Action inconnue."));

        if (NetworkSettings.FromJson(settings).UniFiHost is not { } host)
            return Task.FromResult(new ActionResult(false, "Indique d'abord l'adresse de la console."));

        secrets.Set(UniFiSource.PinKeyPrefix + host.ToLowerInvariant(), null);
        return Task.FromResult(new ActionResult(true, "Certificat oublié : la prochaine connexion mémorisera le nouveau."));
    }

    /// <summary>
    /// Fabrique de production : <c>Pc</c> donne la carte réseau du PC, <c>Freebox</c> la Freebox (par
    /// <paramref name="http"/>, le client HTTP partagé de l'hôte), <c>UniFi</c> la console UniFi, qui a son propre
    /// client HTTP (certificat épinglé, pas de redirection) et ne se sert pas de <paramref name="http"/>.
    /// </summary>
    public static NetworkFactory CreateProduction(HttpClient http) =>
        new((settings, ctx) => settings.Source switch
        {
            NetworkSourceKind.Freebox => new FreeboxSource(http, ctx.Secrets, ctx.Log, time: ctx.Time),
            NetworkSourceKind.UniFi => new UniFiSource(settings.UniFiHost, ctx.Secrets, ctx.Log, time: ctx.Time),
            _ => new NicSource(NicSource.ReadSystemCounters, ctx.Time),
        });

    public IModule Create(ModuleContext context)
    {
        var settings = NetworkSettings.FromJson(context.Settings);
        return new NetworkModule(Descriptor, createSource(settings, context), context.Time, context.Log);
    }
}
