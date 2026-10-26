using Trofeo.Control;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.Services;

/// <summary>
/// La liaison réelle avec le service, par le tube de contrôle. Elle essaie de se connecter dès sa création, puis toutes les
/// 2 s tant que ça échoue ; au troisième échec de suite l'état passe à <see cref="LinkState.ServiceStopped"/> (elle continue
/// d'essayer). Une coupure fait repasser à <see cref="LinkState.Connecting"/>, sans exception vers l'interface. Une fois
/// connectée, elle s'abonne à l'aperçu (4 i/s, ou 12) et relit l'état du service chaque seconde.
/// </summary>
public sealed class ServiceLink : IServiceLink
{
    public const int FailuresBeforeStopped = 3;
    public const int NormalFps = 4;
    public const int RealTimeFps = 12;

    private readonly string _pipeName;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _statusInterval;
    private readonly ServiceStarter _starter;
    private readonly SynchronizationContext? _ui;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly Task _loop;
    private ControlClient? _client;
    private int _fps = NormalFps;
    private int _failures;
    private int _disposed;

    /// <param name="pipeName">Le tube du service.</param>
    /// <param name="retryDelay">Délai entre deux tentatives de connexion (2 s).</param>
    /// <param name="connectTimeout">Durée d'une tentative (1 s).</param>
    /// <param name="statusInterval">Délai entre deux lectures de l'état (1 s).</param>
    /// <param name="starter">Lanceur du service (celui de l'application par défaut).</param>
    /// <param name="ui">Où lever les événements ; par défaut le contexte du fil appelant (le fil de l'interface).</param>
    public ServiceLink(
        string pipeName = ControlPipe.Name, TimeSpan? retryDelay = null, TimeSpan? connectTimeout = null,
        TimeSpan? statusInterval = null, ServiceStarter? starter = null, SynchronizationContext? ui = null)
    {
        _pipeName = pipeName;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(1);
        _statusInterval = statusInterval ?? TimeSpan.FromSeconds(1);
        _starter = starter ?? ServiceStarter.Default;
        _ui = ui ?? SynchronizationContext.Current;
        _loop = Task.Run(RunAsync);
    }

    public LinkState State { get; private set; } = LinkState.Connecting;
    public Welcome? Welcome { get; private set; }
    public ServiceStatus? Status { get; private set; }

    public event Action? Changed;
    public event Action<PreviewFrame>? FrameReceived;

    public void SetRealTimePreview(bool realTime)
    {
        ControlClient? client;
        var fps = realTime ? RealTimeFps : NormalFps;
        lock (_gate)
        {
            _fps = fps;
            client = _client;
        }

        if (client is not null)
            _ = SubscribeQuietlyAsync(client, fps);
    }

    public Task<Reloaded> ReloadAsync() => Current().ReloadAsync(_cts.Token);

    public Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query) =>
        Current().LookupAsync(typeId, fieldKey, query, _cts.Token);

    public Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey) =>
        Current().RunActionAsync(instanceId, actionKey, _cts.Token);

    public Task<bool> RestartModuleAsync(Guid instanceId) => Current().RestartModuleAsync(instanceId, _cts.Token);

    public Task<bool> StartServiceAsync()
    {
        var started = _starter.TryStart();
        if (started)
        {
            // Le service met quelques secondes à ouvrir son tube : on repart de zéro avant de le déclarer arrêté.
            Volatile.Write(ref _failures, 0);
            Update(() => State = LinkState.Connecting, onlyIf: () => State == LinkState.ServiceStopped);
        }

        return Task.FromResult(started);
    }

    private ControlClient Current()
    {
        lock (_gate)
            return _client ?? throw new ControlException(Texts.LinkLost);
    }

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var attemptStarted = DateTime.UtcNow;
            ControlClient? client = null;
            try
            {
                client = await ControlClient.ConnectAsync(_pipeName, _connectTimeout, ct).ConfigureAwait(false);
                Volatile.Write(ref _failures, 0);
                await ServeAsync(client, ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break; // La liaison se ferme : une erreur de dernière minute n'intéresse personne.
            }
            catch (Exception)
            {
                // Pas de service, version de protocole différente, tube qui se ferme, erreur inattendue du système : un échec
                // de plus. Aucune exception ne remonte vers l'interface.
                if (client is null && Interlocked.Increment(ref _failures) >= FailuresBeforeStopped)
                    Update(() => { State = LinkState.ServiceStopped; Welcome = null; Status = null; }, onlyIf: () => State != LinkState.ServiceStopped);
            }
            finally
            {
                if (client is not null)
                {
                    lock (_gate)
                        _client = null;
                    await client.DisposeAsync().ConfigureAwait(false);
                }
            }

            // Connectée puis coupée : on repart à « Connecting » ; ServiceStopped ne se déclare qu'après trois échecs de connexion.
            if (client is not null && !ct.IsCancellationRequested)
                Update(() => { State = LinkState.Connecting; Welcome = null; Status = null; });

            var wait = _retryDelay - (DateTime.UtcNow - attemptStarted);
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Connectée : abonne l'aperçu, publie l'état, puis relit l'état jusqu'à la coupure.</summary>
    private async Task ServeAsync(ControlClient client, CancellationToken ct)
    {
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += () => lost.TrySetResult();
        client.FrameReceived += frame => Post(() => FrameReceived?.Invoke(frame));

        int fps;
        lock (_gate)
        {
            _client = client;
            fps = _fps;
        }

        await client.SubscribePreviewAsync(fps, ct).ConfigureAwait(false);
        var status = await client.GetStatusAsync(ct).ConfigureAwait(false);
        Update(() => { State = LinkState.Connected; Welcome = client.Welcome; Status = status; });

        while (true)
        {
            var finished = await Task.WhenAny(lost.Task, Task.Delay(_statusInterval, ct)).ConfigureAwait(false);
            if (finished == lost.Task)
                return;
            status = await client.GetStatusAsync(ct).ConfigureAwait(false);
            Update(() => Status = status);
        }
    }

    private static async Task SubscribeQuietlyAsync(ControlClient client, int fps)
    {
        try { await client.SubscribePreviewAsync(fps, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ControlException or TimeoutException or OperationCanceledException)
        {
            // La liaison est coupée ou le service ne répond pas : la boucle de connexion s'en occupe.
        }
    }

    /// <summary>Change l'état sur le fil de l'interface, puis lève <see cref="Changed"/>.</summary>
    private void Update(Action change, Func<bool>? onlyIf = null)
    {
        Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || (onlyIf is not null && !onlyIf()))
                return;
            change();
            Changed?.Invoke();
        });
    }

    private void Post(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception) { /* la boucle se termine d'elle-même ; rien à remonter à la fermeture */ }
        _cts.Dispose();
    }
}
