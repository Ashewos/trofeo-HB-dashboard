using SkiaSharp;

namespace Trofeo.ModuleKit;

/// <summary>
/// Un module du tableau de bord. Règles de threads : <see cref="Invalidated"/> peut être levé depuis n'importe
/// quel thread (le minuteur du module, par exemple) ; <see cref="Render"/> est appelé sur le thread d'affichage,
/// qui n'est pas celui du minuteur du module, donc l'état lu au rendu doit pouvoir être lu en concurrence.
/// </summary>
public interface IModule : IDisposable
{
    ModuleDescriptor Descriptor { get; }
    ModuleStatus Status { get; }
    bool IsAnimating { get; }

    /// <summary>
    /// Levé quand le module demande à être redessiné, depuis n'importe quel thread. Un dernier <c>Invalidated</c>
    /// tardif reste possible après <see cref="IDisposable.Dispose"/>.
    /// </summary>
    event EventHandler? Invalidated;

    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Dessine dans <c>ctx.Bounds</c>, le panneau étant déjà peint. Appelé sur le thread d'affichage, pas sur celui
    /// du minuteur du module : l'état lu ici doit être sûr en lecture concurrente.
    /// </summary>
    void Render(SKCanvas canvas, ModuleRenderContext ctx);
}
