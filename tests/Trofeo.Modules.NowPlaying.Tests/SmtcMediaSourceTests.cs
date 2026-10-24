using Trofeo.TestDoubles;

namespace Trofeo.Modules.NowPlaying.Tests;

public class SmtcMediaSourceTests
{
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
}
