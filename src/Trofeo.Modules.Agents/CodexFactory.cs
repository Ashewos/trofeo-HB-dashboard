using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Fabrique du module Codex. <paramref name="createSource"/> choisit la source de l'état (les tests y mettent
/// une source factice).
/// </summary>
public sealed class CodexFactory(Func<ModuleContext, IAgentSource> createSource) : IModuleFactory
{
    private static readonly AgentFlavor Flavor = new(
        "Aucune session Codex ouverte",
        "Codex n'est pas installé sur ce PC",
        CodexMascot.Definition);

    public ModuleDescriptor Descriptor { get; } = new(
        "codex",
        "Codex",
        new GridSize(6, 3),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(6, 3),
            [SizePreset.Ideal] = new GridSize(8, 4),
            [SizePreset.Large] = new GridSize(10, 4),
        });

    /// <summary>Fabrique de production : l'état est lu dans le dossier de Codex de l'utilisateur.</summary>
    public static CodexFactory CreateProduction() =>
        new(context => new CodexSource(CodexHome.Resolve(), context.Time));

    public IModule Create(ModuleContext context) =>
        new AgentModule(Descriptor, Flavor, createSource(context), AgentSettings.Parse(context.Settings), context.Time, context.Log, Random.Shared);
}
