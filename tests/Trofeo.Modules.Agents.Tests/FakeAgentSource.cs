namespace Trofeo.Modules.Agents.Tests;

/// <summary>
/// Source factice : des résultats en file (un relevé ou une exception), servis dans l'ordre ; la file vide, le dernier relevé
/// est servi à nouveau. Enregistre la session demandée à chaque sondage. Sûre entre threads.
/// </summary>
internal sealed class FakeAgentSource : IAgentSource
{
    private readonly object _gate = new();
    private readonly Queue<object> _results = new();
    private readonly List<string?> _selectedIds = [];
    private AgentSnapshot? _last;
    private int _disposed;

    /// <summary>Les sessions demandées, une par sondage, dans l'ordre.</summary>
    public IReadOnlyList<string?> SelectedIds
    {
        get
        {
            lock (_gate)
            {
                return [.. _selectedIds];
            }
        }
    }

    public int Polls => SelectedIds.Count;

    public int Disposed => Volatile.Read(ref _disposed);

    public void Enqueue(AgentSnapshot snapshot)
    {
        lock (_gate)
        {
            _results.Enqueue(snapshot);
        }
    }

    public void EnqueueFailure(Exception exception)
    {
        lock (_gate)
        {
            _results.Enqueue(exception);
        }
    }

    public AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset now)
    {
        lock (_gate)
        {
            _selectedIds.Add(selectedSessionId);
            if (_results.Count > 0)
            {
                var next = _results.Dequeue();
                if (next is Exception exception)
                {
                    throw exception;
                }

                _last = (AgentSnapshot)next;
            }

            return _last ?? throw new InvalidOperationException("Aucun résultat en file.");
        }
    }

    public void Dispose() => Interlocked.Increment(ref _disposed);
}
