namespace Trofeo.Device;

/// <summary>
/// L'écran : poignée de main, envoi d'une image à la fois avec validation, et garde-fou de reprise
/// quand l'écran ne répond plus. Utilisé depuis un seul thread (la boucle d'affichage).
/// </summary>
public sealed class ScreenDevice : IDisposable
{
    public const string AbsentReason = "Écran non branché";
    public const string BusyReason = "Écran utilisé par un autre programme (fermez TRCC)";
    public const string NotRespondingReason = "Écran ne répond plus";

    private const int WriteBlockSize = 4096;
    private const int WriteTimeoutMs = 5000;
    private const int AckSize = 512;
    private const int AckTimeoutMs = 1000;
    private const int HandshakeTimeoutMs = 1000;
    private const int DrainTimeoutMs = 300;
    private const int MaxDrainReads = 64; // garde-fou : un écran qui répond sans fin ne bloque pas la reprise

    private readonly IUsbTransportFactory _factory;
    private readonly TimeProvider _time;
    private IUsbTransport? _transport;

    public ScreenDevice(IUsbTransportFactory factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
        Reason = AbsentReason;
    }

    public ScreenState State { get; private set; } = ScreenState.Disconnected;

    /// <summary>Motif en français quand l'état est <see cref="ScreenState.Disconnected"/>, sinon <c>null</c>.</summary>
    public string? Reason { get; private set; }

    /// <summary>Dernière image envoyée et validée par l'écran.</summary>
    public byte[]? LastFrame { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>Ouvre l'écran, fait la poignée de main, puis renvoie <see cref="LastFrame"/> s'il existe.</summary>
    public bool TryConnect()
    {
        if (State == ScreenState.Ready)
            return true;

        DropTransport();
        IUsbTransport? transport;
        try
        {
            transport = _factory.TryOpen();
        }
        catch (UsbBusyException)
        {
            SetState(ScreenState.Disconnected, BusyReason);
            return false;
        }

        if (transport is null)
        {
            SetState(ScreenState.Disconnected, AbsentReason);
            return false;
        }

        _transport = transport;
        if (!TryHandshake(transport))
        {
            Disconnect(NotRespondingReason);
            return false;
        }

        SetState(ScreenState.Ready, null);
        return LastFrame is null || TrySendFrame(LastFrame);
    }

    /// <summary>
    /// Envoie une image et attend la validation. Sur délai dépassé, lance la reprise puis retente
    /// une seule fois. Renvoie faux si l'écran n'est pas prêt ou si la reprise échoue.
    /// </summary>
    public bool TrySendFrame(byte[] jpeg)
    {
        if (_transport is null || State != ScreenState.Ready)
            return false;

        var packets = LyProtocol.Packetize(jpeg);
        try
        {
            try
            {
                SendPackets(_transport, packets);
            }
            catch (UsbTimeoutException)
            {
                SetState(ScreenState.Recovering, null);
                if (!TryRecover(_transport))
                {
                    Disconnect(NotRespondingReason);
                    return false;
                }

                SendPackets(_transport, packets);
            }
        }
        catch (UsbTimeoutException)
        {
            Disconnect(NotRespondingReason);
            return false;
        }
        catch (IOException)
        {
            Disconnect(AbsentReason);
            return false;
        }

        LastFrame = jpeg;
        SetState(ScreenState.Ready, null);
        return true;
    }

    public void Dispose() => DropTransport();

    private static void SendPackets(IUsbTransport transport, byte[] packets)
    {
        for (var offset = 0; offset < packets.Length; offset += WriteBlockSize)
        {
            var length = Math.Min(WriteBlockSize, packets.Length - offset);
            transport.Write(packets.AsSpan(offset, length), WriteTimeoutMs);
        }

        transport.Read(new byte[AckSize], AckTimeoutMs);
    }

    /// <summary>Reprise : reset des pipes, vidage des réponses en attente, nouvelle poignée de main.</summary>
    private static bool TryRecover(IUsbTransport transport)
    {
        transport.ResetPipes();
        var drain = new byte[AckSize];
        try
        {
            for (var i = 0; i < MaxDrainReads; i++)
                transport.Read(drain, DrainTimeoutMs);
        }
        catch (UsbTimeoutException)
        {
            // La file est vide : c'est le résultat attendu.
        }

        return TryHandshake(transport);
    }

    private static bool TryHandshake(IUsbTransport transport)
    {
        try
        {
            transport.Write(LyProtocol.BuildHandshake(), HandshakeTimeoutMs);
            var reply = new byte[AckSize];
            var read = transport.Read(reply, HandshakeTimeoutMs);
            return LyProtocol.IsValidHandshakeReply(reply.AsSpan(0, read));
        }
        catch (UsbTimeoutException)
        {
            return false;
        }
    }

    private void Disconnect(string reason)
    {
        DropTransport();
        SetState(ScreenState.Disconnected, reason);
    }

    private void DropTransport()
    {
        _transport?.Dispose();
        _transport = null;
    }

    private void SetState(ScreenState state, string? reason)
    {
        if (State == state && Reason == reason)
            return;

        State = state;
        Reason = reason;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
