using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents.Tests;

public sealed class AgentLayoutTests
{
    private static AgentLayoutResult Compute(int columns, int rows, float scale = 1f)
    {
        var rect = GridGeometry.ModuleRect(0, 0, columns, rows);
        return AgentLayout.Compute(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), new GridSize(columns, rows), scale);
    }

    [Fact]
    public void Layout_TabCapacity_8x4()
    {
        var layout = Compute(8, 4);

        // 434 − 2 × 16 − 64 = 338 px pour des onglets de 46 px séparés de 6 px : 6 tiennent, pas 7 (358 px).
        Assert.Equal(338f, layout.Tabs.Height);
        Assert.Equal(6, layout.TabCapacity);
        Assert.Equal(200f, layout.Tabs.Width);
        Assert.Equal(64f, layout.Bar.Height);
        Assert.Equal(64f, layout.Mascot.Width);
        Assert.Equal(64f, layout.Mascot.Height);
    }

    [Fact]
    public void Layout_6x3_NarrowTabs()
    {
        var narrow = Compute(6, 3);
        var wide = Compute(7, 3);

        Assert.Equal(132f, narrow.Tabs.Width);
        Assert.Equal(200f, wide.Tabs.Width);
        Assert.Equal(4, narrow.TabCapacity);
    }

    [Fact]
    public void Layout_Regions_FollowTheBrief()
    {
        var bounds = new SKRect(100, 50, 100 + 941, 50 + 434);
        var layout = AgentLayout.Compute(bounds, new GridSize(8, 4), 1f);

        Assert.Equal(116f, layout.Tabs.Left);
        Assert.Equal(66f, layout.Tabs.Top);
        Assert.Equal(layout.Bar.Top, layout.Tabs.Bottom);
        Assert.Equal(36f, layout.Header.Height);
        Assert.Equal(layout.Header.Bottom, layout.Body.Top);
        Assert.Equal(layout.Bar.Top, layout.Body.Bottom);
        Assert.Equal(bounds.Right - 16f, layout.Body.Right);
        Assert.Equal(bounds.Right - 16f, layout.Bar.Right);
        Assert.Equal(bounds.Bottom - 16f, layout.Bar.Bottom);
        Assert.True(layout.Body.Left > layout.Tabs.Right);
        // Corps de 302 px : 8 lignes de flux de 34 px.
        Assert.Equal(8, layout.FeedCapacity);
    }

    [Fact]
    public void Layout_ScalesWithTextSize()
    {
        var normal = Compute(8, 4);
        var large = Compute(8, 4, 1.2f);

        Assert.Equal(240f, large.Tabs.Width, 0.01f);
        Assert.Equal(76.8f, large.Bar.Height, 0.01f);
        Assert.True(large.TabCapacity < normal.TabCapacity);
        Assert.True(large.FeedCapacity < normal.FeedCapacity);
    }

    [Fact]
    public void Layout_TinyBounds_NeverNegative()
    {
        var layout = AgentLayout.Compute(new SKRect(0, 0, 40, 40), new GridSize(6, 3), 1f);

        Assert.Equal(0, layout.TabCapacity);
        Assert.Equal(0, layout.FeedCapacity);
        Assert.True(layout.Body.Width >= 0 && layout.Body.Height >= 0 && layout.Tabs.Height >= 0);
    }
}
