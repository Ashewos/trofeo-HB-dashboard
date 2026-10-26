using Trofeo.Control;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.Services;

/// <summary>Où en est la liaison avec le service d'affichage.</summary>
public enum LinkState { Connecting, Connected, ServiceStopped }

/// <summary>
/// La liaison de la fenêtre avec le service d'affichage : tout ce que les réglages lui demandent passe par là.
/// Les événements arrivent sur le fil de l'interface.
/// </summary>
public interface IServiceLink : IAsyncDisposable
{
    LinkState State { get; }

    /// <summary>Ce que le service a annoncé à la connexion ; <c>null</c> tant qu'on n'est pas connecté.</summary>
    Welcome? Welcome { get; }

    /// <summary>L'état du service, relu chaque seconde quand on est connecté.</summary>
    ServiceStatus? Status { get; }

    /// <summary><see cref="State"/>, <see cref="Welcome"/> ou <see cref="Status"/> a changé. Sur le fil de l'interface.</summary>
    event Action? Changed;

    /// <summary>Une image de l'écran est arrivée. Sur le fil de l'interface.</summary>
    event Action<PreviewFrame>? FrameReceived;

    /// <summary>Aperçu à 12 images par seconde (<c>true</c>) ou à 4 (<c>false</c>).</summary>
    void SetRealTimePreview(bool realTime);

    /// <summary>
    /// Met l'aperçu en pause (<c>true</c> : plus aucune image demandée, par exemple fenêtre réduite) ou le reprend à 4 ou
    /// 12 images par seconde.
    /// </summary>
    void SetPreviewPaused(bool paused);

    /// <summary>
    /// Les demandes au service. Liaison absente ou fermée (fermeture de la fenêtre comprise) : <see cref="ControlException"/>
    /// avec un message en français, jamais <see cref="ObjectDisposedException"/> ni <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<Reloaded> ReloadAsync();
    Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query);
    Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey);

    /// <summary>Vrai si l'instance existait et a été recréée (à appeler après un changement de secret).</summary>
    Task<bool> RestartModuleAsync(Guid instanceId);

    /// <summary>Lance Trofeo.Display.exe ; faux s'il est introuvable ou ne démarre pas.</summary>
    Task<bool> StartServiceAsync();
}
