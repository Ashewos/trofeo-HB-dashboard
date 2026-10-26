using System.Collections.Concurrent;
using Trofeo.Control;
using Trofeo.ModuleKit;
using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

public class ServiceLinkTests
{
    private static string NewPipeName() => "trofeo-settings-test-" + Guid.NewGuid().ToString("N");

    private static ServiceLink NewLink(string pipe) => new(
        pipe, retryDelay: TimeSpan.FromMilliseconds(60), connectTimeout: TimeSpan.FromMilliseconds(150),
        statusInterval: TimeSpan.FromMilliseconds(80));

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Délai dépassé : " + what);
            await Task.Delay(15);
        }
    }

    private sealed class Handler : IControlHandler
    {
        public readonly ConcurrentQueue<int> Subscriptions = new();

        public Welcome Hello() => new("1.0.0", ControlPipe.ProtocolVersion, []);

        public ServiceStatus GetStatus() => new(
            new ScreenInfo(LinkScreenState.Ready, null), false, [],
            new ProcessInfo(1, 2, 0), new FrameInfo(0, 0, 0), new SensorInfo(false, false, false));

        public Task<Reloaded> ReloadAsync(CancellationToken ct) => Task.FromResult(new Reloaded(true, null, []));
        public Task<bool> RestartModuleAsync(Guid instanceId, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LookupResult>>([]);

        public Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey, CancellationToken ct) =>
            Task.FromResult(new ActionResult(true, "ok"));

        public IDisposable SubscribePreview(int maxFps, Action<PreviewFrame> onFrame)
        {
            Subscriptions.Enqueue(maxFps);
            return new Nothing();
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public async Task Link_ServiceRestarts_Reconnects()
    {
        var pipe = NewPipeName();
        var server = new ControlServer(pipe, new Handler());
        server.Start();
        await using var link = NewLink(pipe);
        var history = new ConcurrentQueue<LinkState>();
        link.Changed += () => history.Enqueue(link.State);

        await WaitUntilAsync(() => link.State == LinkState.Connected, "première connexion");
        Assert.NotNull(link.Welcome);
        await WaitUntilAsync(() => link.Status is not null, "premier état");

        // Le service s'arrête : la fenêtre repasse à « Connecting » sans exception.
        server.Dispose();
        await WaitUntilAsync(() => history.Contains(LinkState.Connecting), "passage à Connecting");

        // Il revient : la fenêtre se reconnecte seule.
        using var again = new ControlServer(pipe, new Handler());
        again.Start();
        await WaitUntilAsync(() => link.State == LinkState.Connected, "reconnexion");
        Assert.NotNull(link.Welcome);
    }

    [Fact]
    public async Task Link_ThreeFailures_IsServiceStopped()
    {
        var pipe = NewPipeName();
        await using var link = NewLink(pipe);
        Assert.Equal(LinkState.Connecting, link.State);

        await WaitUntilAsync(() => link.State == LinkState.ServiceStopped, "arrêt constaté");

        // Le service arrive plus tard : la fenêtre s'en aperçoit.
        using var server = new ControlServer(pipe, new Handler());
        server.Start();
        await WaitUntilAsync(() => link.State == LinkState.Connected, "connexion tardive");
    }

    [Fact]
    public async Task Link_Subscribes4FpsOr12()
    {
        var pipe = NewPipeName();
        var handler = new Handler();
        using var server = new ControlServer(pipe, handler);
        server.Start();
        await using var link = NewLink(pipe);

        await WaitUntilAsync(() => handler.Subscriptions.Count >= 1, "abonnement");
        Assert.Equal(4, handler.Subscriptions.Last());

        link.SetRealTimePreview(true);
        await WaitUntilAsync(() => handler.Subscriptions.Last() == 12, "aperçu à 12 i/s");

        link.SetRealTimePreview(false);
        await WaitUntilAsync(() => handler.Subscriptions.Last() == 4, "retour à 4 i/s");
    }

    [Fact]
    public async Task Link_RealTimeChosenBeforeConnection_SubscribesAt12()
    {
        var pipe = NewPipeName();
        await using var link = NewLink(pipe);
        link.SetRealTimePreview(true);

        var handler = new Handler();
        using var server = new ControlServer(pipe, handler);
        server.Start();
        await WaitUntilAsync(() => handler.Subscriptions.Count >= 1, "abonnement");
        Assert.Equal(12, handler.Subscriptions.First());
    }

    [Fact]
    public async Task Link_NotConnected_RequestsFailWithFrenchMessage()
    {
        await using var link = NewLink(NewPipeName());
        var ex = await Assert.ThrowsAsync<ControlException>(() => link.ReloadAsync());
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }
}
