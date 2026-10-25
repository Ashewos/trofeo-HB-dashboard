using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents.Tests;

/// <summary>Journal de module qui enregistre les messages, niveau en préfixe (« Error : … »). Sûr entre threads.</summary>
internal sealed class FakeModuleLog : IModuleLog
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Info(string message) => Add("Info : " + message);

    public void Warning(string message) => Add("Warning : " + message);

    public void Error(string message) => Add("Error : " + message);

    private void Add(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }
}
