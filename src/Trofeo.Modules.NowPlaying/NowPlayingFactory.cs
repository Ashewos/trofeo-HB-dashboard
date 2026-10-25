using Trofeo.ModuleKit;

namespace Trofeo.Modules.NowPlaying;

/// <summary>
/// Fabrique du module En écoute. <paramref name="createSource"/> reçoit le contexte de l'instance (son journal, en
/// particulier, pour que la source y consigne ses erreurs) et crée la source du module, que celui-ci libère à sa fin.
/// </summary>
public sealed class NowPlayingFactory(Func<ModuleContext, IMediaSource> createSource) : IModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "nowplaying",
        "En écoute",
        new GridSize(3, 1),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(3, 1),
            [SizePreset.Ideal] = new GridSize(4, 2),
            [SizePreset.Large] = new GridSize(6, 2),
        });

    public IModule Create(ModuleContext context) =>
        new NowPlayingModule(Descriptor, createSource(context), context.Time, context.Log);
}
