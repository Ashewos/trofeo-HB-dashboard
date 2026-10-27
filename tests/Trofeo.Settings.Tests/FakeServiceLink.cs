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

    public bool Paused { get; private set; }

    public void SetPreviewPaused(bool paused) => Paused = paused;

    public Task<Reloaded> ReloadAsync()
    {
        ReloadCount++;
        return OnReload();
    }

    /// <summary>Les recherches demandées, dans l'ordre.</summary>
    public List<(string TypeId, string FieldKey, string Query)> Lookups { get; } = [];

    public Func<string, string, string, Task<IReadOnlyList<LookupResult>>> OnLookup { get; set; } =
        (_, _, _) => Task.FromResult<IReadOnlyList<LookupResult>>([]);

    public Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query)
    {
        Lookups.Add((typeId, fieldKey, query));
        return OnLookup(typeId, fieldKey, query);
    }

    /// <summary>Les actions lancées, dans l'ordre.</summary>
    public List<(Guid InstanceId, string ActionKey)> Actions { get; } = [];

    public Func<Guid, string, Task<ActionResult>> OnAction { get; set; } = (_, _) => Task.FromResult(new ActionResult(true, ""));

    public Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey)
    {
        Actions.Add((instanceId, actionKey));
        return OnAction(instanceId, actionKey);
    }

    /// <summary>Les modules rechargés, dans l'ordre.</summary>
    public List<Guid> Restarts { get; } = [];

    public Func<Guid, Task<bool>> OnRestart { get; set; } = _ => Task.FromResult(true);

    public Task<bool> RestartModuleAsync(Guid instanceId)
    {
        Restarts.Add(instanceId);
        return OnRestart(instanceId);
    }

    public Task<bool> StartServiceAsync()
    {
        StartCount++;
        return Task.FromResult(StartResult);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
