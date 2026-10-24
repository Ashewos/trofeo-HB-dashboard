using System.Text.Json;

namespace Trofeo.ModuleKit;

public interface IModuleFactory
{
    ModuleDescriptor Descriptor { get; }
    IModule Create(JsonElement settings, TimeProvider time);
}
