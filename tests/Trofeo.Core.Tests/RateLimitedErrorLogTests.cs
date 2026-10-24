using Microsoft.Extensions.Time.Testing;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public sealed class RateLimitedErrorLogTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> _lines = [];

    private RateLimitedErrorLog NewLog() => new(_time, _lines.Add);

    [Fact]
    public void Report_FirstOccurrence_IsLoggedInFull()
    {
        NewLog().Report("Échec du rendu\nligne 2");

        Assert.Equal(["Échec du rendu\nligne 2"], _lines);
    }

    [Fact]
    public void Report_RepeatsWithin60s_AreSuppressed()
    {
        var log = NewLog();

        for (var i = 0; i < 100; i++)
        {
            log.Report("Échec du rendu");
            _time.Advance(TimeSpan.FromMilliseconds(500)); // 50 s au total
        }

        Assert.Single(_lines);
    }

    [Fact]
    public void Report_After60s_LogsOneSummaryWithCount()
    {
        var log = NewLog();
        log.Report("Échec du rendu\ndétails");
        for (var i = 0; i < 4; i++) log.Report("Échec du rendu\ndétails"); // 4 répétitions

        _time.Advance(TimeSpan.FromSeconds(60));
        log.Report("Échec du rendu\ndétails"); // 5e répétition : le résumé tombe ici
        log.Report("Échec du rendu\ndétails");

        Assert.Equal(2, _lines.Count);
        Assert.Equal("Échec du rendu … répété 5 fois", _lines[1]);
    }

    [Fact]
    public void Report_DifferentMessages_AreTrackedIndependently()
    {
        var log = NewLog();

        log.Report("A");
        log.Report("B");
        log.Report("A");
        log.Report("B");

        Assert.Equal(["A", "B"], _lines);
    }
}
