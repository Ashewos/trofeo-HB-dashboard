using System.Text;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Lit un fichier JSONL qu'un autre processus est en train d'écrire : au premier passage seulement la fin du fichier,
/// ensuite uniquement les octets ajoutés. Le fichier n'est ouvert que le temps d'un appel, en partage complet, pour ne
/// jamais gêner l'écrivain. Le décodage porte sur des lignes entières (octets jusqu'au <c>\n</c>) : une séquence UTF-8
/// coupée entre deux appels n'est donc jamais décodée à moitié.
/// </summary>
/// <param name="path">Chemin du fichier à suivre ; il peut ne pas exister encore.</param>
/// <param name="initialBytes">Octets lus au plus, depuis la fin, au premier passage (ou après une troncature).</param>
/// <param name="maxLineBytes">Taille maximale d'une ligne ; au-delà, la ligne est jetée en entier.</param>
public sealed class TranscriptTail(string path, int initialBytes = 512 * 1024, int maxLineBytes = 1024 * 1024)
{
    /// <summary>Octets lus au plus par appel ; la suite vient à l'appel suivant.</summary>
    private const int MaxReadBytes = 4 * 1024 * 1024;

    private const byte Newline = (byte)'\n';

    private readonly MemoryStream _pending = new();
    private bool _started;
    private long _position;
    // Vrai tant qu'on jette les octets d'une ligne (coupée au départ, ou trop longue) jusqu'au prochain « \n ».
    private bool _discarding;

    /// <summary>
    /// Vrai si le dernier appel est reparti de la fin du fichier (premier appel, ou fichier raccourci) : l'appelant
    /// remet alors son état à zéro avant d'appliquer les lignes.
    /// </summary>
    public bool Restarted { get; private set; }

    /// <summary>Les lignes complètes apparues depuis l'appel précédent (sans « \r\n »). Fichier absent : liste vide.</summary>
    public IReadOnlyList<string> ReadNewLines()
    {
        Restarted = false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (!_started || length < _position)
            {
                Restart(length);
            }

            var toRead = (int)Math.Min(length - _position, MaxReadBytes);
            if (toRead <= 0)
            {
                return [];
            }

            stream.Seek(_position, SeekOrigin.Begin);
            var buffer = new byte[toRead];
            var read = stream.ReadAtLeast(buffer, toRead, throwOnEndOfStream: false);
            _position += read;
            return Split(buffer.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Absent, supprimé ou verrouillé pour un instant : on réessaiera au prochain sondage.
            return [];
        }
    }

    private void Restart(long length)
    {
        _started = true;
        Restarted = true;
        _pending.SetLength(0);
        var start = Math.Max(0, length - initialBytes);
        if (start > 0)
        {
            // On part un octet avant : si c'est un « \n », la première ligne est entière et on la garde ;
            // sinon elle est coupée, et on la jette jusqu'à ce « \n ».
            _position = start - 1;
            _discarding = true;
        }
        else
        {
            _position = 0;
            _discarding = false;
        }
    }

    private List<string> Split(ReadOnlySpan<byte> chunk)
    {
        var lines = new List<string>();
        while (true)
        {
            var newline = chunk.IndexOf(Newline);
            if (newline < 0)
            {
                KeepPartial(chunk);
                return lines;
            }

            if (_discarding)
            {
                _discarding = false;
            }
            else if (_pending.Length + newline <= maxLineBytes)
            {
                lines.Add(Decode(chunk[..newline]));
            }

            _pending.SetLength(0);
            chunk = chunk[(newline + 1)..];
        }
    }

    /// <summary>Garde la fin sans « \n » pour l'appel suivant, ou bascule en rejet si la ligne devient trop longue.</summary>
    private void KeepPartial(ReadOnlySpan<byte> tail)
    {
        if (_discarding || tail.IsEmpty)
        {
            return;
        }

        if (_pending.Length + tail.Length > maxLineBytes)
        {
            _pending.SetLength(0);
            _discarding = true;
            return;
        }

        _pending.Write(tail);
    }

    /// <summary>Décode la ligne complète (début gardé en mémoire + octets du bloc), sans le « \r » final éventuel.</summary>
    private string Decode(ReadOnlySpan<byte> lastPart)
    {
        ReadOnlySpan<byte> line;
        if (_pending.Length == 0)
        {
            line = lastPart;
        }
        else
        {
            _pending.Write(lastPart);
            line = _pending.GetBuffer().AsSpan(0, (int)_pending.Length);
        }

        if (line.Length > 0 && line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        return Encoding.UTF8.GetString(line);
    }
}
