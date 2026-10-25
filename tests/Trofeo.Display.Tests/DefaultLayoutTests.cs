using Trofeo.Core.Layout;
using Trofeo.ModuleKit;
using Trofeo.Modules.Clock;
using Trofeo.Modules.Monitoring;
using Trofeo.Modules.Network;
using Trofeo.Modules.NowPlaying;

namespace Trofeo.Display.Tests;

public sealed class DefaultLayoutTests
{
    /// <summary>
    /// Les descripteurs viennent des vraies fabriques. Elles sont construites sans leurs dépendances : seul le
    /// descripteur est lu, aucun module n'est créé, donc ni matériel, ni réseau, ni contrôles média.
    /// </summary>
    private static IReadOnlyDictionary<string, ModuleDescriptor> RealDescriptors() =>
        new ModuleRegistry(
            [
                new ClockFactory(null!),
                new MonitoringFactory(null!),
                new NowPlayingFactory(null!),
                new NetworkFactory(null!),
            ],
            _ => null!).Descriptors;

    [Fact]
    public void Default_PassesValidationAgainstRealFactories()
    {
        var types = RealDescriptors();

        foreach (var placement in DashboardLayout.Default.Placements)
        {
            Assert.Equal(PlacementError.None, LayoutValidator.Check(DashboardLayout.Default, placement, types));
        }
    }
}
