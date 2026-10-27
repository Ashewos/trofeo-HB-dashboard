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

    /// <summary>Quand elle est posée, toute lecture la lève (coffre verrouillé : le verrou entre processus expire).</summary>
    public Exception? HasFailure { get; set; }

    /// <summary>Les lectures échouent à partir de la première écriture réussie.</summary>
    public bool HasFailsAfterSet { get; set; }

    private bool _written;

    public bool Has(string typeId, string key)
    {
        if (HasFailure is not null || HasFailsAfterSet && _written)
            throw HasFailure ?? new TimeoutException("Le coffre à secrets est verrouillé par un autre processus.");
        return _values.ContainsKey((typeId, key));
    }

    public void Set(string typeId, string key, string? value)
    {
        if (Failure is not null)
            throw Failure;
        Sets.Add((typeId, key, value));
        _written = true;
        if (value is null)
            _values.Remove((typeId, key));
        else
            _values[(typeId, key)] = value;
    }

    public string? Peek(string typeId, string key) => _values.GetValueOrDefault((typeId, key));
}
