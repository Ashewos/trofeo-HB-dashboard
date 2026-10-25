using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Trofeo.Core.Logging;

namespace Trofeo.Core.Tests;

/// <summary>Les écouteurs Trace sont globaux au processus : ces tests ne tournent pas en parallèle entre eux.</summary>
[Collection("Trace")]
public sealed class TraceModuleLogTests
{
    private static readonly Guid InstanceId = new("abcdef12-3456-7890-abcd-ef1234567890");

    private sealed class CapturingListener : TraceListener
    {
        public List<string> Lines { get; } = [];

        public override void Write(string? message) => WriteLine(message);

        public override void WriteLine(string? message)
        {
            lock (Lines) Lines.Add(message ?? "");
        }
    }

    private static List<string> Capture(Action action)
    {
        var listener = new CapturingListener();
        Trace.Listeners.Add(listener);
        try
        {
            action();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        return listener.Lines;
    }

    [Fact]
    public void Error_SameMessage_LoggedOncePerMinute()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        var log = new TraceModuleLog("network", InstanceId, time);

        var lines = Capture(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                log.Error("Échec de connexion");
                time.Advance(TimeSpan.FromSeconds(1));
            }
        });

        Assert.Single(lines, l => l.Contains("Échec de connexion"));

        time.Advance(TimeSpan.FromSeconds(60));
        var later = Capture(() => log.Error("Échec de connexion"));
        Assert.Single(later, l => l.Contains("répété"));
    }

    [Fact]
    public void Messages_ArePrefixedWithTypeAndInstance()
    {
        var log = new TraceModuleLog("network", InstanceId, new FakeTimeProvider());

        var lines = Capture(() =>
        {
            log.Info("démarré");
            log.Warning("lent");
            log.Error("cassé");
        });

        Assert.Contains(lines, l => l.Contains("[network abcdef12] démarré"));
        Assert.Contains(lines, l => l.Contains("[network abcdef12] lent"));
        Assert.Contains(lines, l => l.Contains("[network abcdef12] cassé"));
    }
}
