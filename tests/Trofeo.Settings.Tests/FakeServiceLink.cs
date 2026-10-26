using Trofeo.Control;
using Trofeo.ModuleKit;
using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

/// <summary>Un <see cref="IServiceLink"/> que le test (ou l'aperçu) pilote à la main.</summary>
public sealed class FakeServiceLink : IServiceLink
{
    public LinkState State { get; set; } = LinkState.Connected;
    public Welcome? Welcome { get; set; }
    public ServiceStatus? Status { get; set; }

    public event Action? Changed;
    public event Action<PreviewFrame>? FrameReceived;

    public bool RealTime { get; private set; }
    public int ReloadCount { get; private set; }
    public int StartCount { get; private set; }
    public bool StartResult { get; set; } = true;
    public Func<Task<Reloaded>> OnReload { get; set; } = () => Task.FromResult(new Reloaded(true, null, []));

    /// <summary>Change l'état et prévient les abonnés.</summary>
    public void Set(LinkState state, Welcome? welcome = null, ServiceStatus? status = null)
    {
        State = state;
        Welcome = welcome;
        Status = status;
        Changed?.Invoke();
    }

    public void Push(PreviewFrame frame) => FrameReceived?.Invoke(frame);

    public void SetRealTimePreview(bool realTime) => RealTime = realTime;

    public Task<Reloaded> ReloadAsync()
    {
        ReloadCount++;
        return OnReload();
    }

    public Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query) =>
        Task.FromResult<IReadOnlyList<LookupResult>>([]);

    public Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey) =>
        Task.FromResult(new ActionResult(true, ""));

    public Task<bool> RestartModuleAsync(Guid instanceId) => Task.FromResult(true);

    public Task<bool> StartServiceAsync()
    {
        StartCount++;
        return Task.FromResult(StartResult);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
