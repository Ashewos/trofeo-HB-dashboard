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
    public static LoopConfig Build(DashboardLayout layout, ISystemFonts fonts, Func<string, IBackground?>? video, Action<string> warn)
    {
        var appearance = layout.Appearance ?? AppearanceSettings.Default;
        var theme = AppearanceResolver.Resolve(appearance, fonts, warn);
        var background = BackgroundFactory.Create(appearance.Background, theme, video, warn);
        return new LoopConfig(layout, theme, background);
    }
}
