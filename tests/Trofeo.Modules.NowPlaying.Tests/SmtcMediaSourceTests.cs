using Microsoft.Extensions.Time.Testing;
using Windows.Media.Control;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.NowPlaying.Tests;

public class SmtcMediaSourceTests
{
    private static readonly MediaSnapshot Sample = new(
        "Titre", "Artiste", "Spotify", TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, null);

    /// <summary>
    /// Fumée : se connecte aux vrais contrôles média de Windows. Passe que quelque chose joue ou non, et n'affirme rien
    /// sur le contenu ; ne doit ni lever ni bloquer.
    /// </summary>
    [Fact]
    public async Task StartAsync_RealSmtc_DoesNotThrow()
    {
        using var source = new SmtcMediaSource(new FakeModuleLog());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await source.StartAsync(cts.Token);

        _ = source.Current; // éventuellement null : rien ne joue
    }

    [Fact]
    public async Task Refresh_HungRead_IsAbandonedAfterTimeoutAndNextRefreshPublishes()
    {
        var time = new FakeTimeProvider();
        var log = new FakeModuleLog();
        var calls = 0;
        using var source = new SmtcMediaSource(log, time, null, ct =>
            Interlocked.Increment(ref calls) == 1
                ? new TaskCompletionSource<MediaSnapshot?>().Task // ne rend jamais la main, même annulé
                : Task.FromResult<MediaSnapshot?>(Sample));

        var hung = source.RefreshAsync();
        Assert.False(hung.IsCompleted);

        time.Advance(SmtcMediaSource.RefreshTimeout);
        await hung.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(source.Current);
        Assert.Contains(log.Entries, e => e.StartsWith("Error : ") && e.Contains("pas de réponse"));

        // Le verrou a été rendu : le relevé suivant n'est pas bloqué et publie.
        await source.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Sample, source.Current);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Refresh_Failure_DropsStaleValueAndNextEventPublishes()
    {
        var log = new FakeModuleLog();
        var calls = 0;
        using var source = new SmtcMediaSource(log, new FakeTimeProvider(), null, _ =>
            Interlocked.Increment(ref calls) switch
            {
                1 => Task.FromResult<MediaSnapshot?>(Sample),
                2 => Task.FromException<MediaSnapshot?>(new InvalidOperationException("boum")),
                _ => Task.FromResult<MediaSnapshot?>(Sample with { Title = "Suivant" }),
            });
        var changes = 0;
        source.Changed += (_, _) => Interlocked.Increment(ref changes);

        await source.RefreshAsync();
        Assert.Equal("Titre", source.Current?.Title);

        await source.RefreshAsync();
        Assert.Null(source.Current); // pas de valeur périmée qui passe pour fraîche
        Assert.Contains(log.Entries, e => e.Contains("boum"));
        Assert.Equal(2, changes); // la disparition est annoncée

        await source.RefreshAsync();
        Assert.Equal("Suivant", source.Current?.Title);
        Assert.Equal(3, calls); // l'échec n'a pas été compté comme couvert : le relevé suivant relit vraiment
    }

    [Fact]
    public async Task StartAsync_RequestFails_RetriesEveryThirtySecondsWithoutThrowing()
    {
        var time = new FakeTimeProvider();
        var log = new FakeModuleLog();
        var calls = 0;
        using var source = new SmtcMediaSource(log, time, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromException<GlobalSystemMediaTransportControlsSessionManager>(new InvalidOperationException("muet"));
        }, null);

        await source.StartAsync(CancellationToken.None);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Contains(log.Entries, e => e.StartsWith("Error : ") && e.Contains("muet"));

        time.Advance(SmtcMediaSource.RetryPeriod - TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref calls));

        await AdvanceUntilCallsAsync(time, () => Volatile.Read(ref calls), 2);
        await AdvanceUntilCallsAsync(time, () => Volatile.Read(ref calls), 3);
        Assert.Equal(3, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task StartAsync_RequestHangs_TimesOutThenRetries()
    {
        var time = new FakeTimeProvider();
        var calls = 0;
        using var source = new SmtcMediaSource(new FakeModuleLog(), time, _ =>
        {
            Interlocked.Increment(ref calls);
            return new TaskCompletionSource<GlobalSystemMediaTransportControlsSessionManager>().Task;
        }, null);

        var start = source.StartAsync(CancellationToken.None);
        Assert.False(start.IsCompleted);

        time.Advance(SmtcMediaSource.CallTimeout);
        await start.WaitAsync(TimeSpan.FromSeconds(5));

        await AdvanceUntilCallsAsync(time, () => Volatile.Read(ref calls), 2);
    }

    [Fact]
    public async Task StartAsync_CallerCancelled_Throws()
    {
        using var source = new SmtcMediaSource(new FakeModuleLog(), new FakeTimeProvider(),
            _ => new TaskCompletionSource<GlobalSystemMediaTransportControlsSessionManager>().Task, null);
        using var cts = new CancellationTokenSource();

        var start = source.StartAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>Avance l'horloge par pas de 30 s jusqu'à ce que le compteur atteigne <paramref name="target"/> (le retour au minuteur suivant est asynchrone).</summary>
    private static async Task AdvanceUntilCallsAsync(FakeTimeProvider time, Func<int> calls, int target)
    {
        for (var i = 0; i < 100 && calls() < target; i++)
        {
            time.Advance(SmtcMediaSource.RetryPeriod);
            await Task.Delay(30);
        }
        Assert.Equal(target, calls());
    }
}
