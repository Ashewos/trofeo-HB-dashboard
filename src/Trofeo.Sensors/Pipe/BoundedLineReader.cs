namespace Trofeo.Sensors;

/// <summary>
/// Lecteur de lignes (séparées par <c>\n</c>) à mémoire bornée : une ligne plus longue que la limite lève
/// <see cref="InvalidDataException"/> sans jamais être accumulée en entier, contrairement à
/// <see cref="StreamReader.ReadLineAsync()"/> qui n'a pas de limite.
/// </summary>
internal sealed class BoundedLineReader(Stream stream, int maxLineBytes)
{
    private readonly byte[] _chunk = new byte[4096];
    private readonly byte[] _line = new byte[maxLineBytes];
    private int _pos, _len, _lineLength;

    /// <summary>Dernière ligne lue, sans <c>\n</c> ni <c>\r</c> final. Valable jusqu'au prochain appel.</summary>
    public ReadOnlyMemory<byte> Line => _line.AsMemory(0, _lineLength);

    /// <summary>Lit la ligne suivante ; faux en fin de flux (une ligne finale sans <c>\n</c> est perdue).</summary>
    public async ValueTask<bool> ReadLineAsync(CancellationToken ct)
    {
        _lineLength = 0;
        while (true)
        {
            if (_pos == _len)
            {
                _len = await stream.ReadAsync(_chunk, ct);
                _pos = 0;
                if (_len == 0) return false;
            }

            var available = _chunk.AsSpan(_pos, _len - _pos);
            var newline = available.IndexOf((byte)'\n');
            var take = newline < 0 ? available.Length : newline;
            if (_lineLength + take > maxLineBytes) throw new InvalidDataException("Ligne de relevé trop longue.");

            available[..take].CopyTo(_line.AsSpan(_lineLength));
            _lineLength += take;
            if (newline < 0)
            {
                _pos = _len;
                continue;
            }

            _pos += newline + 1;
            if (_lineLength > 0 && _line[_lineLength - 1] == (byte)'\r') _lineLength--;
            return true;
        }
    }
}
