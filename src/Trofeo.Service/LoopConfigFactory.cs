using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;

namespace Trofeo.Display;

/// <summary>Fabrique ce que la boucle applique (thème et fond compris) à partir d'une disposition.</summary>
public static class LoopConfigFactory
{
    /// <summary>
    /// AppearanceResolver + BackgroundFactory, comme Program.Run aujourd'hui ; les avertissements vont à <paramref name="warn"/>.
    /// La boucle devient propriétaire du fond du résultat.
    /// </summary>
    /// <param name="video">Ouvre un fond vidéo (Media Foundation, côté Windows) ; <c>null</c> : les vidéos ne sont pas prises en charge.</param>
    /// <param name="current">
    /// Ce que la boucle applique en ce moment, s'il y en a un. Son fond est gardé (même instance, la vidéo n'est pas rouverte)
    /// quand le fond demandé et le fond par défaut du thème résolu n'ont pas changé : l'opacité, la police, la garde de
    /// lisibilité ou un autre thème de même fond par défaut ne le touchent pas. Les avertissements de ce fond sont redonnés à
    /// <paramref name="warn"/>.
    /// </param>
    public static LoopConfig Build(
        DashboardLayout layout, ISystemFonts fonts, Func<string, IBackground?>? video, Action<string> warn, LoopConfig? current = null)
    {
        var appearance = layout.Appearance ?? AppearanceSettings.Default;
        var theme = AppearanceResolver.Resolve(appearance, fonts, warn);

        if (current is not null
            && Equals(current.Layout.Appearance?.Background, appearance.Background)
            && Equals(current.Theme.DefaultBackground, theme.DefaultBackground))
        {
            foreach (var message in current.BackgroundWarnings) warn(message);
            return new LoopConfig(layout, theme, current.Background) { BackgroundWarnings = current.BackgroundWarnings };
        }

        var backgroundWarnings = new List<string>();
        var background = BackgroundFactory.Create(appearance.Background, theme, video, message =>
        {
            backgroundWarnings.Add(message);
            warn(message);
        });
        return new LoopConfig(layout, theme, background) { BackgroundWarnings = backgroundWarnings };
    }
}
