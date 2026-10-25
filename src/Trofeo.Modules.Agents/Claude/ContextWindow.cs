using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents;

/// <summary>Part de la fenêtre de contexte d'une session qui est remplie.</summary>
public static class ContextWindow
{
    private const long DefaultWindow = 200_000;
    private const long LargeWindow = 1_000_000;
    private static readonly TimeSpan FreshCapture = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Le pourcentage rempli, borné à [0, 100], ou <c>null</c> si on ne peut pas le savoir. Une capture de la barre d'état
    /// de moins de 60 s qui donne ce pourcentage fait foi ; sinon on divise <paramref name="tokens"/> par la fenêtre :
    /// celle de la capture si elle la donne, sinon 1 000 000 (modèle en <c>[1m]</c>, ou plus de 200 000 jetons déjà
    /// utilisés), sinon 200 000. <paramref name="capture"/> doit être celle de la même session.
    /// </summary>
    public static double? Percent(long? tokens, string? modelId, StatusLineRecord? capture, DateTimeOffset now)
    {
        if (capture is { ContextUsedPercent: { } used } && now - capture.CapturedAt < FreshCapture)
        {
            return Clamp(used);
        }

        if (tokens is not { } count)
        {
            return null;
        }

        long window;
        if (capture is { ContextWindowSize: > 0 })
        {
            window = capture.ContextWindowSize.Value;
        }
        else if ((modelId?.Contains("[1m]", StringComparison.OrdinalIgnoreCase) ?? false) || count > DefaultWindow)
        {
            window = LargeWindow;
        }
        else
        {
            window = DefaultWindow;
        }

        return Clamp(count * 100.0 / window);
    }

    private static double Clamp(double percent) => double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
}
