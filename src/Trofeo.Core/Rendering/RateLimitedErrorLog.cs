using System.Diagnostics;

namespace Trofeo.Core.Rendering;

/// <summary>
/// Journal d'erreurs qui ne se répète pas : un message identique est écrit en entier la première fois, puis
/// au plus une ligne de synthèse (« … répété N fois ») par minute. Évite qu'un module cassé, redessiné douze
/// fois par seconde, remplisse le journal. Utilisable depuis plusieurs threads.
/// </summary>
public sealed class RateLimitedErrorLog
{
    private const int MaxTrackedMessages = 64;
    private const int MaxSummaryLength = 200;
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(60);

    private sealed class Entry(long lastEmit)
    {
        public long LastEmit = lastEmit;
        public int Suppressed;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly TimeProvider _time;
    private readonly Action<string> _write;

    /// <param name="time">Source de temps (mockable en test).</param>
    /// <param name="write">Destination des lignes ; par défaut <see cref="Trace.TraceError(string)"/>.</param>
    public RateLimitedErrorLog(TimeProvider time, Action<string>? write = null)
    {
        _time = time;
        _write = write ?? (m => Trace.TraceError(m));
    }

    /// <summary>Signale une erreur ; le message complet sert d'identité (même texte = même erreur).</summary>
    public void Report(string message)
    {
        string? line = null;
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (!_entries.TryGetValue(message, out var entry))
            {
                if (_entries.Count >= MaxTrackedMessages) _entries.Clear(); // borne la mémoire
                _entries[message] = new Entry(now);
                line = message;
            }
            else
            {
                entry.Suppressed++;
                if (_time.GetElapsedTime(entry.LastEmit, now) >= SummaryInterval)
                {
                    line = $"{Head(message)} … répété {entry.Suppressed} fois";
                    entry.Suppressed = 0;
                    entry.LastEmit = now;
                }
            }
        }

        if (line is not null) _write(line);
    }

    private static string Head(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        var head = end < 0 ? message : message[..end];
        return head.Length > MaxSummaryLength ? head[..MaxSummaryLength] : head;
    }
}
