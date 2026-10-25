using Trofeo.ModuleKit;
using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Fabrique du module Claude Code. <paramref name="createSource"/> choisit la source de l'état (les tests y mettent
/// une source factice).
/// </summary>
public sealed class ClaudeCodeFactory(Func<ModuleContext, IAgentSource> createSource) : IModuleFactory
{
    private static readonly AgentFlavor Flavor = new(
        "Aucune session Claude Code ouverte",
        "Claude Code n'est pas installé sur ce PC",
        ClaudeMascot.Definition);

    public ModuleDescriptor Descriptor { get; } = new(
        "claudecode",
        "Claude Code",
        new GridSize(6, 3),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(6, 3),
            [SizePreset.Ideal] = new GridSize(8, 4),
            [SizePreset.Large] = new GridSize(10, 4),
        });

    /// <summary>Fabrique de production : l'état est lu dans le dossier de Claude Code de l'utilisateur.</summary>
    public static ClaudeCodeFactory CreateProduction() =>
        new(_ => new ClaudeSource(ClaudeHome.Resolve(), ClaudeSource.IsProcessAlive));

    public IModule Create(ModuleContext context) =>
        new AgentModule(Descriptor, Flavor, createSource(context), AgentSettings.Parse(context.Settings), context.Time, context.Log, Random.Shared);
}
