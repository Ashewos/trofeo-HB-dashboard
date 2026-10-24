using System.Diagnostics;
using Trofeo.Core.Layout;
using Trofeo.ModuleKit;

namespace Trofeo.Display;

/// <summary>Les types de modules connus de l'application, indexés par leur identifiant.</summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, IModuleFactory> _factories;

    public ModuleRegistry(IEnumerable<IModuleFactory> factories)
    {
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
            return factory.Create(p.Settings, time);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Module ignoré : échec de création de « {p.TypeId} » ({p.InstanceId}) : {ex}");
            return null;
        }
    }
}
