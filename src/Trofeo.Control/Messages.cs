using System.Text.Json;
using System.Text.Json.Serialization;
using Trofeo.ModuleKit;

namespace Trofeo.Control;

/// <summary>Un type de module tel que la fenêtre de réglage le connaît : de quoi construire son formulaire.</summary>
public sealed record ModuleTypeInfo(string TypeId, string DisplayName, GridSize MinSize,
    IReadOnlyDictionary<SizePreset, GridSize> Presets, bool AcceptsCommands, SettingsSchema Schema);

/// <summary>Réponse à la poignée de main : version du service et types de modules disponibles.</summary>
public sealed record Welcome(string ServiceVersion, int ProtocolVersion, IReadOnlyList<ModuleTypeInfo> Types);

public enum LinkScreenState { Ready, Recovering, Disconnected }

public sealed record ScreenInfo(LinkScreenState State, string? Reason);

public sealed record ModuleState(Guid InstanceId, string TypeId, ModuleHealth Health, string? ActionMessage, IReadOnlyList<string> Notices);

public sealed record ProcessInfo(long PrivateBytes, long WorkingSetBytes, double CpuPercent);

public sealed record FrameInfo(int FramesSent, double RenderMs, double SendMs);

public sealed record SensorInfo(bool PawnIoInstalled, bool HelperInstalled, bool HelperConnected);

public sealed record ServiceStatus(ScreenInfo Screen, bool Emulated, IReadOnlyList<ModuleState> Modules,
    ProcessInfo Process, FrameInfo Frames, SensorInfo Sensors);

public sealed record Reloaded(bool Applied, string? Error, IReadOnlyList<string> Warnings);

/// <summary>Une image de l'écran (JPEG) et la rotation que l'écran lui applique, en degrés.</summary>
public sealed record PreviewFrame(byte[] Jpeg, int Rotation);

/// <summary>Ce que le service sait faire pour la fenêtre de réglage. Implémenté côté service.</summary>
public interface IControlHandler
{
    Welcome Hello();
    ServiceStatus GetStatus();
    Task<Reloaded> ReloadAsync(CancellationToken ct);

    /// <summary>Vrai si l'instance existait et a été recréée (un module gardé au rechargement ne relit pas ses secrets).</summary>
    Task<bool> RestartModuleAsync(Guid instanceId, CancellationToken ct);

    Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query, CancellationToken ct);
    Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey, CancellationToken ct);

    /// <summary>
    /// <paramref name="onFrame"/> est appelé depuis le fil d'affichage : il ne doit jamais bloquer.
    /// Le tableau d'octets d'une image n'est plus modifié après l'appel (il est écrit plus tard, sur un autre fil).
    /// </summary>
    IDisposable SubscribePreview(int maxFps, Action<PreviewFrame> onFrame);
}

/// <summary>Erreur renvoyée par le service, ou connexion perdue ; le message est affichable.</summary>
public sealed class ControlException(string message) : Exception(message);

/// <summary>Types de requête du protocole.</summary>
internal static class MessageTypes
{
    public const string Hello = "hello";
    public const string Status = "status";
    public const string Reload = "reload";
    public const string Restart = "restart";
    public const string Lookup = "lookup";
    public const string Action = "action";
    public const string Preview = "preview";
    public const string Error = "error";
}

/// <summary>Requête JSON : seuls les champs utiles au type de requête sont renseignés.</summary>
internal sealed record Request(int Id, string Type)
{
    public string? TypeId { get; init; }
    public string? FieldKey { get; init; }
    public string? Query { get; init; }
    public Guid? InstanceId { get; init; }
    public string? ActionKey { get; init; }
    public int? MaxFps { get; init; }
}

/// <summary>Réponse JSON : <see cref="ReplyTo"/> reprend l'identifiant de la requête ; type « error » pour un échec.</summary>
internal sealed record Response(int ReplyTo, string Type)
{
    public string? Message { get; init; }
    public Welcome? Welcome { get; init; }
    public ServiceStatus? Status { get; init; }
    public Reloaded? Reloaded { get; init; }
    public bool? Restarted { get; init; }
    public IReadOnlyList<LookupResult>? Results { get; init; }
    public ActionResult? Action { get; init; }
}

internal static class ControlJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        // Les énumérations voyagent en texte.
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Le « kind » d'un champ de schéma n'est pas forcément la première propriété.
        AllowOutOfOrderMetadataProperties = true,
    };
}
