using Trofeo.TestDoubles;

namespace Trofeo.Device.Tests;

public sealed class ScreenDeviceTests
{
    private static byte[] Jpeg(int length, byte fill = 0xAB) => Enumerable.Repeat(fill, length).ToArray();

    private static ScreenDevice NewDevice(FakeUsbTransportFactory factory) => new(factory, TimeProvider.System);

    /// <summary>Écran sain : la connexion vide la file (délai dépassé) puis la poignée de main réussit.</summary>
    private static FakeUsbTransport ReadyTransport() => new FakeUsbTransport().EnqueueReadTimeout().EnqueueHandshakeReply();

    [Fact]
    public void Connect_Handshake_Ready()
    {
        var transport = ReadyTransport();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        var changes = 0;
        device.StateChanged += (_, _) => changes++;

        Assert.True(device.TryConnect());

        Assert.Equal(ScreenState.Ready, device.State);
        Assert.Null(device.Reason);
        Assert.Equal(1, changes);
        Assert.Collection(
            transport.Calls,
            c => Assert.Equal(UsbCallKind.ResetPipes, c.Kind),
            c => AssertCall(c, UsbCallKind.Read, 512, 300), // vidage : file vide
            c =>
            {
                AssertCall(c, UsbCallKind.Write, 2048, 1000);
                Assert.Equal(LyProtocol.BuildHandshake(), c.Data);
            },
            c => AssertCall(c, UsbCallKind.Read, 512, 1000));
    }

    [Fact]
    public void Connect_FrozenScreen_ResetsAndDrainsBeforeHandshake()
    {
        var stale = new byte[512];
        stale[0] = 0x03; // validation restée en file par un processus précédent
        var transport = new FakeUsbTransport()
            .EnqueueRead(stale)
            .EnqueueReadTimeout()
            .EnqueueHandshakeReply();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));

        Assert.True(device.TryConnect());

        Assert.Equal(ScreenState.Ready, device.State);
        Assert.Collection(
            transport.Calls,
            c => Assert.Equal(UsbCallKind.ResetPipes, c.Kind),
            c => AssertCall(c, UsbCallKind.Read, 512, 300), // réponse périmée
            c => AssertCall(c, UsbCallKind.Read, 512, 300), // file vidée
            c => AssertCall(c, UsbCallKind.Write, 2048, 1000), // poignée de main
            c => AssertCall(c, UsbCallKind.Read, 512, 1000));
    }

    [Fact]
    public void Connect_InvalidHandshakeReply_Disconnected()
    {
        var transport = new FakeUsbTransport().EnqueueReadTimeout().EnqueueRead(new byte[512]);
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));

        Assert.False(device.TryConnect());

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran ne répond plus", device.Reason);
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void Send_WritesIn4096Blocks_ThenReadsAck()
    {
        var transport = ReadyTransport().EnqueueAck();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();
        transport.Calls.Clear();
        var jpeg = Jpeg(5000);

        Assert.True(device.TrySendFrame(jpeg));

        Assert.Collection(
            transport.Calls,
            c => AssertCall(c, UsbCallKind.Write, 4096, 5000),
            c => AssertCall(c, UsbCallKind.Write, 2048, 5000),
            c => AssertCall(c, UsbCallKind.Read, 512, 1000));
        var sent = transport.Writes.SelectMany(w => w.Data!).ToArray();
        Assert.Equal(6144, sent.Length); // 12 paquets de 512 octets
        Assert.Equal(LyProtocol.Packetize(jpeg), sent);
        Assert.Equal(ScreenState.Ready, device.State);
        Assert.Equal(jpeg, device.LastFrame);
    }

    [Fact]
    public void AckTimeout_RecoversAndResendsOnce()
    {
        var transport = ReadyTransport()
            .EnqueueReadTimeout() // validation manquante
            .EnqueueAck() // une réponse en attente à vider
            .EnqueueReadTimeout() // file vidée
            .EnqueueHandshakeReply()
            .EnqueueAck(); // validation de l'image renvoyée
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();
        transport.Calls.Clear();
        var states = new List<ScreenState>();
        device.StateChanged += (_, _) => states.Add(device.State);
        var jpeg = Jpeg(1000);

        Assert.True(device.TrySendFrame(jpeg));

        Assert.Collection(
            transport.Calls,
            c => AssertCall(c, UsbCallKind.Write, 2048, 5000), // image
            c => AssertCall(c, UsbCallKind.Read, 512, 1000), // délai dépassé
            c => Assert.Equal(UsbCallKind.ResetPipes, c.Kind),
            c => AssertCall(c, UsbCallKind.Read, 512, 300), // vidage : une réponse
            c => AssertCall(c, UsbCallKind.Read, 512, 300), // vidage : délai dépassé
            c => AssertCall(c, UsbCallKind.Write, 2048, 1000), // poignée de main
            c => AssertCall(c, UsbCallKind.Read, 512, 1000),
            c => AssertCall(c, UsbCallKind.Write, 2048, 5000), // image renvoyée
            c => AssertCall(c, UsbCallKind.Read, 512, 1000));
        Assert.Equal(new[] { ScreenState.Recovering, ScreenState.Ready }, states);
        Assert.Equal(ScreenState.Ready, device.State);
        Assert.Equal(jpeg, device.LastFrame);
    }

    [Fact]
    public void WriteTimeout_Recovers()
    {
        var transport = ReadyTransport()
            .EnqueueReadTimeout() // vidage
            .EnqueueHandshakeReply()
            .EnqueueAck();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();
        transport.FailNextWrites(1);

        Assert.True(device.TrySendFrame(Jpeg(1000)));

        Assert.Equal(ScreenState.Ready, device.State);
    }

    [Fact]
    public void SecondTimeoutAfterRecovery_Disconnected()
    {
        var transport = ReadyTransport()
            .EnqueueReadTimeout() // première validation manquante
            .EnqueueReadTimeout() // vidage
            .EnqueueHandshakeReply()
            .EnqueueReadTimeout(); // validation manquante après reprise
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();
        transport.Calls.Clear();

        Assert.False(device.TrySendFrame(Jpeg(1000)));

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran ne répond plus", device.Reason);
        Assert.True(transport.IsDisposed);
        Assert.Null(device.LastFrame);
        Assert.Equal(1, transport.Calls.Count(c => c.Kind == UsbCallKind.ResetPipes)); // une seule reprise
    }

    [Fact]
    public void RecoveryFails_Disconnected()
    {
        var transport = ReadyTransport()
            .EnqueueReadTimeout() // validation manquante
            .EnqueueReadTimeout() // vidage
            .EnqueueRead(new byte[512]); // poignée de main refusée
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();

        Assert.False(device.TrySendFrame(Jpeg(1000)));

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran ne répond plus", device.Reason);
        Assert.True(transport.IsDisposed);
        Assert.False(device.TrySendFrame(Jpeg(1000))); // plus de transport : aucun envoi
    }

    [Fact]
    public void OpenDenied_ReportsBusy()
    {
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueBusy());

        Assert.False(device.TryConnect());

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran utilisé par un autre programme (fermez TRCC)", device.Reason);
    }

    [Fact]
    public void Absent_ReportsNotPlugged()
    {
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueAbsent());

        Assert.False(device.TryConnect());

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran non branché", device.Reason);
    }

    [Fact]
    public void Reconnects_AndResendsLastFrame()
    {
        var first = ReadyTransport()
            .EnqueueAck() // image A validée
            .EnqueueReadTimeout() // image B : validation manquante
            .EnqueueReadTimeout() // vidage
            .EnqueueRead(new byte[512]); // poignée de main refusée
        var second = ReadyTransport().EnqueueAck();
        var factory = new FakeUsbTransportFactory().EnqueueTransport(first).EnqueueAbsent().EnqueueTransport(second);
        using var device = NewDevice(factory);
        var frameA = Jpeg(1000, 0x11);
        device.TryConnect();
        device.TrySendFrame(frameA);
        Assert.False(device.TrySendFrame(Jpeg(1000, 0x22)));
        Assert.Equal(ScreenState.Disconnected, device.State);

        Assert.False(device.TryConnect());
        Assert.Equal("Écran non branché", device.Reason);

        Assert.True(device.TryConnect());

        Assert.Equal(ScreenState.Ready, device.State);
        Assert.Equal(frameA, device.LastFrame);
        var resent = second.Writes.Skip(1).SelectMany(w => w.Data!).ToArray(); // après la poignée de main
        Assert.Equal(LyProtocol.Packetize(frameA), resent);
    }

    [Fact]
    public void LastFrame_IsACopy()
    {
        var transport = ReadyTransport().EnqueueAck();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();
        var jpeg = Jpeg(1000, 0x33);

        device.TrySendFrame(jpeg);
        Array.Fill(jpeg, (byte)0x44);

        Assert.NotNull(device.LastFrame);
        Assert.All(device.LastFrame!, b => Assert.Equal(0x33, b));
    }

    [Fact]
    public void Send_WriteIOException_DisconnectsAsUnplugged()
    {
        var transport = ReadyTransport();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();
        transport.FailNextWritesWithIOException(1);

        Assert.False(device.TrySendFrame(Jpeg(1000)));

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran non branché", device.Reason);
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void Send_AckReadIOException_DisconnectsAsUnplugged()
    {
        var transport = ReadyTransport().EnqueueReadIOException();
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));
        device.TryConnect();

        Assert.False(device.TrySendFrame(Jpeg(1000)));

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran non branché", device.Reason);
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void Connect_HandshakeIOException_ReturnsFalse()
    {
        var transport = new FakeUsbTransport().EnqueueReadTimeout().FailNextWritesWithIOException(1);
        using var device = NewDevice(new FakeUsbTransportFactory().EnqueueTransport(transport));

        Assert.False(device.TryConnect());

        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.Equal("Écran non branché", device.Reason);
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void Connect_FailedResend_NeverAnnouncesReady()
    {
        var first = ReadyTransport().EnqueueAck();
        var second = ReadyTransport().EnqueueReadIOException(); // renvoi : écran débranché
        var factory = new FakeUsbTransportFactory().EnqueueTransport(first).EnqueueTransport(second);
        using var device = NewDevice(factory);
        device.TryConnect();
        device.TrySendFrame(Jpeg(1000));
        first.FailNextWritesWithIOException(1);
        Assert.False(device.TrySendFrame(Jpeg(1000))); // perte de l'écran
        var states = new List<ScreenState>();
        device.StateChanged += (_, _) => states.Add(device.State);

        Assert.False(device.TryConnect());

        Assert.DoesNotContain(ScreenState.Ready, states);
        Assert.Equal(ScreenState.Disconnected, device.State);
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public void SendFrame_WhenDisconnected_ReturnsFalse()
    {
        using var device = NewDevice(new FakeUsbTransportFactory());

        Assert.False(device.TrySendFrame(Jpeg(10)));
        Assert.Null(device.LastFrame);
    }

    private static void AssertCall(UsbCall call, UsbCallKind kind, int length, int timeoutMs)
    {
        Assert.Equal(kind, call.Kind);
        Assert.Equal(length, call.Length);
        Assert.Equal(timeoutMs, call.TimeoutMs);
    }
}
