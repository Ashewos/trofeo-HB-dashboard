using Trofeo.Device;

namespace Trofeo.TestDoubles;

/// <summary>Un appel reçu par <see cref="FakeUsbTransport"/>, dans l'ordre.</summary>
public enum UsbCallKind
{
    Write,
    Read,
    ResetPipes,
}

/// <summary>Appel enregistré : <paramref name="Length"/> est la taille écrite ou la taille du tampon de lecture.</summary>
public sealed record UsbCall(UsbCallKind Kind, int Length, int TimeoutMs, byte[]? Data = null);

/// <summary>
/// Transport USB scriptable : les lectures consomment une file de réponses (ou de délais dépassés),
/// les écritures peuvent être programmées pour dépasser leur délai. Tous les appels sont enregistrés.
/// </summary>
public sealed class FakeUsbTransport : IUsbTransport
{
    /// <summary>Réponse d'une lecture : octets à renvoyer, ou <c>null</c> pour un délai dépassé.</summary>
    private readonly Queue<byte[]?> _reads = new();
    private int _failingWrites;

    public List<UsbCall> Calls { get; } = [];

    public bool IsDisposed { get; private set; }

    /// <summary>Appels d'écriture, avec leurs données.</summary>
    public IEnumerable<UsbCall> Writes => Calls.Where(c => c.Kind == UsbCallKind.Write);

    public FakeUsbTransport EnqueueRead(byte[] response)
    {
        _reads.Enqueue(response);
        return this;
    }

    public FakeUsbTransport EnqueueReadTimeout()
    {
        _reads.Enqueue(null);
        return this;
    }

    /// <summary>Réponse valide à la poignée de main (annexe A).</summary>
    public FakeUsbTransport EnqueueHandshakeReply()
    {
        var reply = new byte[512];
        reply[0] = 0x03;
        reply[1] = 0xFF;
        reply[8] = 0x01;
        return EnqueueRead(reply);
    }

    /// <summary>Validation d'une image : 512 octets quelconques.</summary>
    public FakeUsbTransport EnqueueAck() => EnqueueRead(new byte[512]);

    /// <summary>Les <paramref name="count"/> prochaines écritures lèvent <see cref="UsbTimeoutException"/>.</summary>
    public FakeUsbTransport FailNextWrites(int count)
    {
        _failingWrites = count;
        return this;
    }

    public void Write(ReadOnlySpan<byte> data, int timeoutMs)
    {
        Calls.Add(new UsbCall(UsbCallKind.Write, data.Length, timeoutMs, data.ToArray()));
        if (_failingWrites > 0)
        {
            _failingWrites--;
            throw new UsbTimeoutException();
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        Calls.Add(new UsbCall(UsbCallKind.Read, buffer.Length, timeoutMs));
        if (_reads.Count == 0)
            throw new InvalidOperationException("FakeUsbTransport : aucune réponse de lecture programmée.");

        var response = _reads.Dequeue();
        if (response is null)
            throw new UsbTimeoutException();

        var length = Math.Min(response.Length, buffer.Length);
        response.AsSpan(0, length).CopyTo(buffer);
        return length;
    }

    public void ResetPipes() => Calls.Add(new UsbCall(UsbCallKind.ResetPipes, 0, 0));

    public void Dispose() => IsDisposed = true;
}

/// <summary>Fabrique scriptable : chaque <see cref="TryOpen"/> consomme un résultat ; file vide = écran absent.</summary>
public sealed class FakeUsbTransportFactory : IUsbTransportFactory
{
    private readonly Queue<Func<IUsbTransport?>> _outcomes = new();

    public int OpenCount { get; private set; }

    /// <summary>Le prochain <see cref="TryOpen"/> renvoie ce transport.</summary>
    public FakeUsbTransportFactory EnqueueTransport(IUsbTransport transport)
    {
        _outcomes.Enqueue(() => transport);
        return this;
    }

    /// <summary>Le prochain <see cref="TryOpen"/> renvoie <c>null</c> (écran absent).</summary>
    public FakeUsbTransportFactory EnqueueAbsent()
    {
        _outcomes.Enqueue(() => null);
        return this;
    }

    /// <summary>Le prochain <see cref="TryOpen"/> lève <see cref="UsbBusyException"/>.</summary>
    public FakeUsbTransportFactory EnqueueBusy()
    {
        _outcomes.Enqueue(() => throw new UsbBusyException());
        return this;
    }

    public IUsbTransport? TryOpen()
    {
        OpenCount++;
        return _outcomes.Count == 0 ? null : _outcomes.Dequeue()();
    }
}
