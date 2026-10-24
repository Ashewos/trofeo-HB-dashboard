using System.Diagnostics;
using Trofeo.Core.Layout;
using Trofeo.Core.Logging;
using Trofeo.ModuleKit;

namespace Trofeo.Display;

/// <summary>Les types de modules connus de l'application, indexés par leur identifiant.</summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, IModuleFactory> _factories;
    private readonly Func<string, ISecretStore> _secretsFor;

    /// <param name="factories">Les fabriques des types de modules connus.</param>
    /// <param name="secretsFor">Le coffre à secrets d'un type de module, indexé par son identifiant de type.</param>
    public ModuleRegistry(IEnumerable<IModuleFactory> factories, Func<string, ISecretStore> secretsFor)
    {
        _secretsFor = secretsFor;
        _factories = factories.ToDictionary(f => f.Descriptor.TypeId);
        Descriptors = _factories.ToDictionary(f => f.Key, f => f.Value.Descriptor);
    }

    public IReadOnlyDictionary<string, ModuleDescriptor> Descriptors { get; }

    /// <summary>Crée le module d'un emplacement. Type inconnu ou création en échec : journalisé, <c>null</c>.</summary>
    public IModule? Create(ModulePlacement p, TimeProvider time)
    {
        if (!_factories.TryGetValue(p.TypeId, out var factory))
        {
            Trace.TraceWarning($"Module ignoré : type inconnu « {p.TypeId} » ({p.InstanceId}).");
            return null;
        }

        try
        {
            var context = new ModuleContext(
                p.InstanceId, p.Settings, time, new TraceModuleLog(p.TypeId, p.InstanceId, time), _secretsFor(p.TypeId));
            return factory.Create(context);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Module ignoré : échec de création de « {p.TypeId} » ({p.InstanceId}) : {ex}");
            return null;
        }
    }
}
