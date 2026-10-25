using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Trofeo.Sensors.Tests;

/// <summary>Tube réel (Windows) entre le serveur élevé et le client, avec un nom unique par test. Temps réel, délais généreux.</summary>
public class SensorPipeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private sealed class FakeReader(SensorSnapshot snapshot) : ISensorReader
    {
        public SensorSnapshot Read() => snapshot;
        public void Dispose() { }
    }

    // Lève pendant les premiers appels, puis rend le relevé.
    private sealed class FlakyReader(SensorSnapshot snapshot, int failures) : ISensorReader
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);

        public SensorSnapshot Read() =>
            Interlocked.Increment(ref _reads) <= failures ? throw new InvalidOperationException("capteur en panne") : snapshot;

        public void Dispose() { }
    }

    private static SensorSnapshot Sample() => new("CPU X", "GPU Y",
        new Dictionary<string, double> { [SensorKeys.CpuLoad] = 34.0, [SensorKeys.GpuTemp] = 61.5 }, Elevated: true);

    private static string NewName() => "trofeo-test-" + Guid.NewGuid().ToString("N");

    private static async Task<T> Within<T>(Task<T> task, string what)
    {
        try { return await task.WaitAsync(Wait); }
        catch (TimeoutException) { throw new Xunit.Sdk.XunitException($"Délai dépassé : {what}"); }
    }

    private static async Task Within(Task task, string what)
    {
        try { await task.WaitAsync(Wait); }
        catch (TimeoutException) { throw new Xunit.Sdk.XunitException($"Délai dépassé : {what}"); }
    }

    // Faux serveur : tube sortant sans ACL ; le test écrit lui-même ce qu'il veut une fois le client connecté.
    private static async Task<NamedPipeServerStream> FakeServerAsync(string name, CancellationToken ct)
    {
        var server = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync(ct);
        return server;
    }

    [Fact]
    public async Task Client_TrustedServer_ReceivesSnapshot()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var server = new SensorPipeServer(name, new FakeReader(Sample()), TimeProvider.System);
        var serverTask = server.RunAsync(cts.Token);
        var sawConnectedPipe = false;
        using var client = new SensorPipeClient(name, pipe => { sawConnectedPipe = pipe.IsConnected; return true; }, TimeProvider.System);

        var tcs = new TaskCompletionSource<SensorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Received += (_, s) => tcs.TrySetResult(s);
        var clientTask = client.RunAsync(cts.Token);
        var snap = await Within(tcs.Task, "aucun relevé reçu");

        Assert.True(sawConnectedPipe);
        Assert.True(client.IsConnected);
        Assert.Equal("CPU X", snap.CpuName);
        Assert.Equal("GPU Y", snap.GpuName);
        Assert.True(snap.Elevated);
        Assert.Equal(34.0, snap.Values[SensorKeys.CpuLoad]);
        Assert.Equal(61.5, snap.Values[SensorKeys.GpuTemp]);

        await cts.CancelAsync();
        await Within(Task.WhenAll(serverTask, clientTask), "arrêt");
    }

    [Fact]
    public async Task Client_UntrustedServer_IgnoresSnapshots()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var server = new SensorPipeServer(name, new FakeReader(Sample()), TimeProvider.System);
        var serverTask = server.RunAsync(cts.Token);
        using var client = new SensorPipeClient(name, _ => false, TimeProvider.System);
        var received = 0;
        client.Received += (_, _) => Interlocked.Increment(ref received);
        var clientTask = client.RunAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(0, received);
        Assert.False(client.IsConnected);

        await cts.CancelAsync();
        await Within(Task.WhenAll(serverTask, clientTask), "arrêt");
    }

    [Fact]
    public async Task Client_MalformedLine_IsSkipped()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var serverTask = FakeServerAsync(name, cts.Token);
        using var client = new SensorPipeClient(name, _ => true, TimeProvider.System);
        var received = new List<SensorSnapshot>();
        var gotOne = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Received += (_, s) => { lock (received) received.Add(s); gotOne.TrySetResult(); };
        var clientTask = client.RunAsync(cts.Token);

        await using var server = await Within(serverTask, "connexion du client");
        var bytes = Encoding.UTF8.GetBytes("{oops\n"
            + "{\"cpuName\":\"CPU X\",\"gpuName\":null,\"elevated\":false,\"values\":{\"cpu.load\":12.5}}\n");
        await server.WriteAsync(bytes, cts.Token);
        await server.FlushAsync(cts.Token);
        await Within(gotOne.Task, "relevé valide non reçu");
        await Task.Delay(300); // laisse le temps à un éventuel second relevé

        lock (received)
        {
            var only = Assert.Single(received);
            Assert.Equal(12.5, only.Values[SensorKeys.CpuLoad]);
            Assert.Null(only.GpuName);
            Assert.False(only.Elevated);
        }

        await cts.CancelAsync();
        await Within(clientTask, "arrêt");
    }

    [Fact]
    public async Task Client_OversizedLine_Disconnects()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var serverTask = FakeServerAsync(name, cts.Token);
        using var client = new SensorPipeClient(name, _ => true, TimeProvider.System);
        var received = 0;
        client.Received += (_, _) => Interlocked.Increment(ref received);
        var clientTask = client.RunAsync(cts.Token);

        await using var server = await Within(serverTask, "connexion du client");
        // Une « ligne » sans fin : le client doit couper, ce qui fait échouer l'écriture côté serveur.
        var chunk = new byte[16384];
        Array.Fill(chunk, (byte)'a');
        var broke = false;
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline && !broke)
        {
            try { await server.WriteAsync(chunk, cts.Token); await server.FlushAsync(cts.Token); }
            catch (IOException) { broke = true; }
            await Task.Delay(20);
        }

        Assert.True(broke, "le client aurait dû couper la connexion");
        Assert.False(client.IsConnected);
        Assert.Equal(0, received);

        await cts.CancelAsync();
        await Within(clientTask, "arrêt");
    }

    [Fact]
    public async Task Server_ClientLeaves_AcceptsNextClient()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var server = new SensorPipeServer(name, new FakeReader(Sample()), TimeProvider.System);
        var serverTask = server.RunAsync(cts.Token);

        async Task<string> ReadFirstLine()
        {
            // Le serveur ne remarque le départ du premier client qu'à son écriture suivante (au plus 1 s) : on attend plus longtemps.
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(4000, cts.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            return (await reader.ReadLineAsync(cts.Token))!;
        }

        var first = await Within(ReadFirstLine(), "premier client");
        var second = await Within(ReadFirstLine(), "second client");

        Assert.Contains("\"cpu.load\":34", first);
        Assert.Equal(first, second);

        await cts.CancelAsync();
        await Within(serverTask, "arrêt du serveur");
    }

    [Fact]
    public async Task Server_DeniesWriteAccessToClients()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var serverTask = new SensorPipeServer(name, new FakeReader(Sample()), TimeProvider.System).RunAsync(cts.Token);

        // Un utilisateur authentifié ne reçoit que le droit de lecture : ouvrir le tube en écriture est refusé.
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pipe.ConnectAsync(2000, cts.Token));

        await cts.CancelAsync();
        await Within(serverTask, "arrêt du serveur");
    }

    [Fact]
    public async Task Server_ReaderThrowsOnce_KeepsServing()
    {
        var name = NewName();
        using var cts = new CancellationTokenSource();
        var reader = new FlakyReader(Sample(), failures: 1);
        var serverTask = new SensorPipeServer(name, reader, TimeProvider.System).RunAsync(cts.Token);
        using var client = new SensorPipeClient(name, _ => true, TimeProvider.System);
        var tcs = new TaskCompletionSource<SensorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Received += (_, s) => tcs.TrySetResult(s);
        var clientTask = client.RunAsync(cts.Token);

        var snap = await Within(tcs.Task, "aucun relevé après l'erreur du lecteur");

        Assert.Equal(34.0, snap.Values[SensorKeys.CpuLoad]);
        Assert.True(reader.Reads >= 2); // le premier relevé a échoué, la connexion est restée ouverte
        Assert.False(serverTask.IsCompleted);

        await cts.CancelAsync();
        await Within(Task.WhenAll(serverTask, clientTask), "arrêt");
    }

    [Fact]
    public void BuildSecurity_DeniesNetwork_AndGrantsReadOnlyToAuthenticatedUsers()
    {
        var rules = SensorPipeServer.BuildSecurity().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>().ToList();

        var network = Assert.Single(rules, r => r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)));
        Assert.Equal(AccessControlType.Deny, network.AccessControlType);
        Assert.Equal(PipeAccessRights.FullControl, network.PipeAccessRights);

        var users = Assert.Single(rules, r => r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)));
        Assert.Equal(AccessControlType.Allow, users.AccessControlType);
        Assert.False(users.PipeAccessRights.HasFlag(PipeAccessRights.WriteData));
    }

    [Fact]
    public void Parse_UnknownFieldsAndMissingValues_AreHandled()
    {
        var parsed = SensorPipe.TryParse(Encoding.UTF8.GetBytes("{\"extra\":{\"a\":1},\"cpuName\":null,\"elevated\":true}"));

        Assert.NotNull(parsed);
        Assert.Null(parsed.CpuName);
        Assert.Empty(parsed.Values);
        Assert.True(parsed.Elevated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("{\"values\":{\"cpu.load\":\"abc\"}}")]
    public void Parse_Invalid_ReturnsNull(string line) =>
        Assert.Null(SensorPipe.TryParse(Encoding.UTF8.GetBytes(line)));

    [Fact]
    public void Serialize_UsesDocumentedFormat_AndRoundTrips()
    {
        var bytes = SensorPipe.Serialize(Sample() with { Values = new Dictionary<string, double> { ["cpu.load"] = 34.0, ["bad"] = double.NaN } });
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal("{\"cpuName\":\"CPU X\",\"gpuName\":\"GPU Y\",\"elevated\":true,\"values\":{\"cpu.load\":34}}\n", text);
        var back = SensorPipe.TryParse(bytes.AsSpan(0, bytes.Length - 1));
        Assert.Equal(34.0, back!.Values["cpu.load"]);
    }
}
