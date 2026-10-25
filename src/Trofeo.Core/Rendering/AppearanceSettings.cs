namespace Trofeo.Core.Rendering;

/// <summary>
/// Réglages d'apparence choisis par l'utilisateur, écrits dans <c>layout.json</c> (clé <c>Appearance</c>).
/// </summary>
/// <param name="Theme">Identifiant du thème ; défaut « classique ».</param>
/// <param name="PanelOpacity">Opacité du fond des panneaux (0 à 1) ; <c>null</c> : celle du thème.</param>
/// <param name="Font">Nom de famille de la police ; <c>null</c> : celle du thème.</param>
/// <param name="Background">Fond de l'écran ; <c>null</c> : celui du thème.</param>
public sealed record AppearanceSettings(
    string Theme = "classique", float? PanelOpacity = null, string? Font = null, BackgroundSettings? Background = null)
{
    /// <summary>Aucun réglage : Classique tel quel.</summary>
    public static AppearanceSettings Default { get; } = new();
}
