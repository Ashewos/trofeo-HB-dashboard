using SkiaSharp;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Les zones du module : colonne des onglets, en-tête, corps (flux ou vue d'ensemble), barre du bas et carré de la mascotte.
/// <paramref name="TabCapacity"/> et <paramref name="FeedCapacity"/> sont les nombres de lignes entières qui tiennent.
/// </summary>
internal sealed record AgentLayoutResult(
    SKRect Tabs, SKRect Header, SKRect Body, SKRect Bar, SKRect Mascot, int TabCapacity, int FeedCapacity);

/// <summary>Mise en page du module ; toutes les dimensions sont multipliées par l'échelle du texte.</summary>
internal static class AgentLayout
{
    internal const float Margin = 16f;
    internal const float BarHeight = 64f;
    internal const float WideTabsWidth = 200f;
    internal const float NarrowTabsWidth = 132f;
    internal const float TabHeight = 46f;
    internal const float TabGap = 6f;
    internal const float ColumnGap = 16f;
    internal const float HeaderHeight = 36f;
    internal const float FeedLineHeight = 34f;

    /// <summary>Colonnes à partir desquelles la colonne des onglets passe de 132 à 200 px.</summary>
    private const int WideTabsFromColumns = 7;

    public static AgentLayoutResult Compute(SKRect bounds, GridSize size, float textScale)
    {
        var margin = Margin * textScale;
        var barHeight = BarHeight * textScale;
        var tabsWidth = (size.Columns >= WideTabsFromColumns ? WideTabsWidth : NarrowTabsWidth) * textScale;

        var left = bounds.Left + margin;
        var top = bounds.Top + margin;
        var right = Math.Max(left, bounds.Right - margin);
        var bottom = bounds.Bottom - margin;
        var barTop = Math.Max(top, bottom - barHeight);
        bottom = Math.Max(bottom, barTop);

        var tabsRight = Math.Min(right, left + tabsWidth);
        var contentLeft = Math.Min(right, tabsRight + ColumnGap * textScale);
        var headerBottom = Math.Min(barTop, top + HeaderHeight * textScale);

        var tabs = new SKRect(left, top, tabsRight, barTop);
        var header = new SKRect(contentLeft, top, right, headerBottom);
        var body = new SKRect(contentLeft, headerBottom, right, barTop);
        var bar = new SKRect(left, barTop, right, bottom);
        var mascot = new SKRect(left, barTop, Math.Min(right, left + BarHeight * textScale), bottom);

        var tabStep = (TabHeight + TabGap) * textScale;
        var tabCapacity = (int)Math.Floor((tabs.Height + TabGap * textScale) / tabStep + 1e-4);
        var feedCapacity = (int)Math.Floor(body.Height / (FeedLineHeight * textScale) + 1e-4);
        return new AgentLayoutResult(tabs, header, body, bar, mascot, Math.Max(0, tabCapacity), Math.Max(0, feedCapacity));
    }
}
