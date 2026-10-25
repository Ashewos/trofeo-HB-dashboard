using Trofeo.ModuleKit;

namespace Trofeo.TestDoubles;

/// <summary>Journal de module qui enregistre les messages, niveau en préfixe (« Info : … »).</summary>
public sealed class FakeModuleLog : IModuleLog
{
    public List<string> Entries { get; } = [];

    public void Info(string message) => Entries.Add("Info : " + message);

    public void Warning(string message) => Entries.Add("Warning : " + message);

    public void Error(string message) => Entries.Add("Error : " + message);
}
