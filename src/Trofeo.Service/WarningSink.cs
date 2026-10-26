namespace Trofeo.Display;

/// <summary>
/// Recueille les avertissements d'une fabrication de configuration (apparence, fond) pour les rendre dans la réponse au
/// rechargement. Sûr entre fils.
/// </summary>
public sealed class WarningSink
{
    private readonly object _gate = new();
    private List<string> _warnings = [];

    public void Warn(string message)
    {
        lock (_gate) _warnings.Add(message);
    }

    /// <summary>Rend les avertissements recueillis depuis le dernier appel, dans l'ordre, et les oublie.</summary>
    public IReadOnlyList<string> Drain()
    {
        lock (_gate)
        {
            var taken = _warnings;
            _warnings = [];
            return taken;
        }
    }
}
