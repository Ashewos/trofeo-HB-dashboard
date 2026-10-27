using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

/// <summary>Un coffre pour les tests : en mémoire, qui note chaque écriture. DPAPI n'existe pas sous Linux.</summary>
public sealed class FakeSecretAccess : ISecretAccess
{
    private readonly Dictionary<(string TypeId, string Key), string> _values = [];

    /// <summary>Les écritures, dans l'ordre (valeur <c>null</c> : effacement).</summary>
    public List<(string TypeId, string Key, string? Value)> Sets { get; } = [];

    /// <summary>Quand elle est posée, toute écriture la lève.</summary>
    public Exception? Failure { get; set; }

    public bool Has(string typeId, string key) => _values.ContainsKey((typeId, key));

    public void Set(string typeId, string key, string? value)
    {
        if (Failure is not null)
            throw Failure;
        Sets.Add((typeId, key, value));
        if (value is null)
            _values.Remove((typeId, key));
        else
            _values[(typeId, key)] = value;
    }

    public string? Peek(string typeId, string key) => _values.GetValueOrDefault((typeId, key));
}
