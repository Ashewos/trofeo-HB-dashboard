using System.Diagnostics;
using System.Text;

namespace Trofeo.Core.Logging;

/// <summary>
/// Écrit chaque message du <see cref="Trace"/> sur une ligne horodatée, dans un fichier lisible pendant
/// l'exécution. Au-delà de <c>maxBytes</c> (au démarrage comme à l'écriture), le fichier devient <c>.1</c>
/// (une seule copie conservée) et un nouveau fichier commence. Si le fichier ne peut pas être rouvert, les
/// messages sont perdus sans exception et une nouvelle ouverture est tentée au plus une fois par minute.
/// </summary>
public sealed class FileTraceListener : TraceListener
{
    /// <summary>Taille à partir de laquelle le journal est renouvelé.</summary>
    public const long DefaultMaxBytes = 1_000_000;

    /// <summary>Délai minimal entre deux tentatives de réouverture du journal.</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly TimeProvider _time;
    private readonly Func<string, Stream> _open;
    private Stream? _stream;
    private StreamWriter? _writer;
    private bool _retryPending;
    private long _nextRetryAt; // horodatage du TimeProvider avant lequel on ne rouvre pas le journal

    public FileTraceListener(string path, long maxBytes = DefaultMaxBytes, TimeProvider? time = null)
        : this(path, maxBytes, time, OpenAppend)
    {
    }

    /// <param name="open">Ouvre le flux d'écriture du journal ; sert aux tests à simuler une réouverture en échec.</param>
    internal FileTraceListener(string path, long maxBytes, TimeProvider? time, Func<string, Stream> open)
    {
        _path = path;
        _maxBytes = maxBytes;
        _time = time ?? TimeProvider.System;
        _open = open;
        Open();
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message) => WriteEntry("Info", message);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        WriteEntry(Level(eventType), message);

    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
        WriteEntry(Level(eventType), args is { Length: > 0 } && format is not null ? string.Format(format, args) : format);

    private static string Level(TraceEventType type) => type switch
    {
        TraceEventType.Critical or TraceEventType.Error => "Erreur",
        TraceEventType.Warning => "Avert",
        _ => "Info",
    };

    private static FileStream OpenAppend(string path) =>
        new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

    private void Open()
    {
        Attach(_open(_path));
        if (_stream!.Length > _maxBytes) Rotate();
    }

    private void Attach(Stream stream)
    {
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>
    /// Ferme le fichier, le déplace en <c>.1</c> et en rouvre un neuf. Si le déplacement échoue (fichier verrouillé),
    /// on continue dans le même fichier. Si la réouverture échoue, il n'y a plus de journal jusqu'à la prochaine
    /// tentative (<see cref="TryReopen"/>).
    /// </summary>
    private void Rotate()
    {
        _writer?.Dispose(); // ferme aussi le flux
        _writer = null;
        _stream = null;
        try
        {
            File.Move(_path, _path + ".1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Journal verrouillé par un lecteur : on rouvre le fichier courant, la rotation sera retentée plus tard.
        }

        TryReopen();
    }

    /// <summary>Rouvre le journal ; en cas d'échec, le prochain essai n'aura lieu que dans <see cref="RetryInterval"/>.</summary>
    private void TryReopen()
    {
        try
        {
            Attach(_open(_path));
            _retryPending = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _writer = null;
            _stream = null;
            _retryPending = true;
            _nextRetryAt = _time.GetTimestamp() + (long)(RetryInterval.TotalSeconds * _time.TimestampFrequency);
        }
    }

    private void WriteEntry(string level, string? message)
    {
        lock (_gate)
        {
            try
            {
                if (_writer is null)
                {
                    if (!_retryPending || _time.GetTimestamp() < _nextRetryAt) return;
                    TryReopen();
                    if (_writer is null) return;
                }

                if (_stream!.Length > _maxBytes)
                {
                    Rotate();
                    if (_writer is null) return;
                }

                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                // Le journal ne doit jamais faire tomber le service.
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
                _retryPending = false;
            }
        }

        base.Dispose(disposing);
    }
}
