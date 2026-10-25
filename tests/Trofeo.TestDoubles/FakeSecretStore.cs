using Trofeo.ModuleKit;

namespace Trofeo.TestDoubles;

/// <summary>Coffre à secrets en mémoire.</summary>
public sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];

    public string? Get(string key) => _values.GetValueOrDefault(key);

    public void Set(string key, string? value)
    {
        if (value is null) _values.Remove(key);
        else _values[key] = value;
    }
}
