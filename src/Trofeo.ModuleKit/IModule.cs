using SkiaSharp;

namespace Trofeo.ModuleKit;

public interface IModule : IDisposable
{
    ModuleDescriptor Descriptor { get; }
    ModuleStatus Status { get; }
    bool IsAnimating { get; }

    /// <summary>Levé quand le module demande à être redessiné.</summary>
    event EventHandler? Invalidated;

    Task StartAsync(CancellationToken ct);

    /// <summary>Dessine dans <c>ctx.Bounds</c>, le panneau étant déjà peint.</summary>
    void Render(SKCanvas canvas, ModuleRenderContext ctx);
}
