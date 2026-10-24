namespace Trofeo.ModuleKit;

public interface IModuleFactory
{
    ModuleDescriptor Descriptor { get; }
    IModule Create(ModuleContext context);
}
