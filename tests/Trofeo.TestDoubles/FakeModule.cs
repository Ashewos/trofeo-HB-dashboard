using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.TestDoubles;

/// <summary>Module factice configurable : action de rendu, exception éventuelle, appels enregistrés.</summary>
public sealed class FakeModule : IModule
{
    private readonly Action<SKCanvas, ModuleRenderContext>? _render;

    public FakeModule(Action<SKCanvas, ModuleRenderContext>? render = null, bool throwOnRender = false)
    {
        _render = render;
        ThrowOnRender = throwOnRender;
    }

    /// <summary>Si vrai, <see cref="Render"/> lève une <see cref="InvalidOperationException"/>.</summary>
    public bool ThrowOnRender { get; set; }

    public List<ModuleRenderContext> RenderCalls { get; } = [];

    public ModuleDescriptor Descriptor { get; init; } = new(
        "fake", "Faux module", new GridSize(1, 1), new Dictionary<SizePreset, GridSize>());

    public ModuleStatus Status { get; set; } = new(ModuleHealth.Online);

    public bool IsAnimating { get; set; }

    public event EventHandler? Invalidated;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public void Render(SKCanvas canvas, ModuleRenderContext ctx)
    {
        RenderCalls.Add(ctx);
        if (ThrowOnRender)
        {
            throw new InvalidOperationException("Échec de rendu simulé.");
        }

        _render?.Invoke(canvas, ctx);
    }

    /// <summary>Simule une demande de redessin du module.</summary>
    public void RaiseInvalidated() => Invalidated?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}
