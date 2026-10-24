using System.Diagnostics;
using System.Text;

namespace Trofeo.Display;

/// <summary>
/// Écrit chaque message du <see cref="Trace"/> sur une ligne horodatée, dans un fichier lisible pendant
/// l'exécution. Au-delà de <c>maxBytes</c> (au démarrage comme à l'écriture), le fichier devient <c>.1</c>
/// (une seule copie conservée) et un nouveau fichier commence.
/// </summary>
public sealed class FileTraceListener : TraceListener
{
    /// <summary>Taille à partir de laquelle le journal est renouvelé.</summary>
    public const long DefaultMaxBytes = 1_000_000;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private FileStream? _stream;
    private StreamWriter? _writer;

    public FileTraceListener(string path, long maxBytes = DefaultMaxBytes)
    {
        _path = path;
        _maxBytes = maxBytes;
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

    private void Open()
    {
        var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        if (stream.Length > _maxBytes) Rotate();
    }

    /// <summary>Ferme le fichier, le déplace en <c>.1</c> et en rouvre un neuf. Si le déplacement échoue (fichier verrouillé), on continue dans le même fichier.</summary>
    private void Rotate()
    {
        _writer?.Dispose(); // ferme aussi le flux
        try
        {
            File.Move(_path, _path + ".1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Journal verrouillé par un lecteur : on rouvre le fichier courant, la rotation sera retentée plus tard.
        }

        var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    private void WriteEntry(string level, string? message)
    {
        lock (_gate)
        {
            try
            {
                if (_writer is null) return;
                if (_stream!.Length > _maxBytes) Rotate();
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
            }
        }

        base.Dispose(disposing);
    }
}
