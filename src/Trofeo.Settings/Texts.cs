using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace Trofeo.Settings;

/// <summary>
/// Les textes de la fenêtre, lus dans <c>Resources/Strings.resx</c> : une propriété par clé (le XAML les lit par
/// <c>{x:Static}</c>). Un test vérifie que chaque clé a sa propriété, et que chacune sert. Écrite à la main, parce que
/// le générateur de ressources de Roslyn n'existe qu'en préversion.
/// </summary>
public static class Texts
{
    private static readonly ResourceManager Manager = new("Trofeo.Settings.Resources.Strings", typeof(Texts).Assembly);

    private static string Get([CallerMemberName] string key = "") => Manager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>Titre de la fenêtre.</summary>
    public static string WindowTitle => Get();
    /// <summary>Nom du produit, à gauche de l'en-tête.</summary>
    public static string AppName => Get();
    /// <summary>Nom accessible de la barre d'onglets.</summary>
    public static string TabsName => Get();
    /// <summary>Disposition</summary>
    public static string TabLayout => Get();
    /// <summary>Personnalisation</summary>
    public static string TabAppearance => Get();
    /// <summary>Modules</summary>
    public static string TabModules => Get();
    /// <summary>Raccourcis</summary>
    public static string TabShortcuts => Get();
    /// <summary>Paramètres</summary>
    public static string TabSettings => Get();
    /// <summary>Pastille de l'écran : l'écran répond.</summary>
    public static string ScreenOnline => Get();
    /// <summary>Pastille de l'écran : aucun écran.</summary>
    public static string ScreenIdle => Get();
    /// <summary>Pastille de l'écran : le service reprend la liaison.</summary>
    public static string ScreenRecovering => Get();
    /// <summary>Pastille ambre : le service compose sans écran branché.</summary>
    public static string EmulatedMode => Get();
    /// <summary>Pastille pendant la connexion au service.</summary>
    public static string LinkConnecting => Get();
    /// <summary>Pastille quand le service ne tourne pas.</summary>
    public static string LinkStopped => Get();
    /// <summary>Nom accessible de la bande.</summary>
    public static string StripName => Get();
    /// <summary>Texte de la bande tant qu'aucune image n'est arrivée.</summary>
    public static string StripWaiting => Get();
    /// <summary>Le service d'affichage ne tourne pas. Sans lui, l'écran reste éteint et rien ne peut être réglé.</summary>
    public static string ServiceStoppedMessage => Get();
    /// <summary>Démarrer le service</summary>
    public static string StartService => Get();
    /// <summary>Le service d'affichage est introuvable. Lance Trofeo.Display.exe toi-même.</summary>
    public static string ServiceNotFound => Get();
    /// <summary>Erreur quand la liaison est coupée.</summary>
    public static string LinkLost => Get();
    /// <summary>Erreur quand la liaison se ferme pendant une demande (fermeture de la fenêtre, coupure).</summary>
    public static string LinkCut => Get();
    /// <summary>La disposition a changé en dehors des réglages : elle a été relue.</summary>
    public static string LayoutChangedOutside => Get();
    /// <summary>Le service refuse sans donner de motif.</summary>
    public static string ReloadRefused => Get();
    /// <summary>{0} : la cause.</summary>
    public static string SaveFailed => Get();
    /// <summary>Titre de la liste des modules placés.</summary>
    public static string LayoutOnScreen => Get();
    /// <summary>Titre de la liste des modules retirés de l'écran.</summary>
    public static string LayoutShelved => Get();
    /// <summary>Bouton qui ouvre la liste des types de modules.</summary>
    public static string LayoutAddModule => Get();
    /// <summary>Remet un module retiré sur l'écran.</summary>
    public static string LayoutRestore => Get();
    /// <summary>Supprime pour de bon un module retiré.</summary>
    public static string LayoutForget => Get();
    /// <summary>Confirmation avant d'oublier un module retiré.</summary>
    public static string LayoutForgetConfirm => Get();
    /// <summary>Renonce à oublier un module retiré.</summary>
    public static string LayoutForgetKeep => Get();
    /// <summary>Consigne sous la bande.</summary>
    public static string LayoutHint => Get();
    /// <summary>Annule la dernière modification de la disposition.</summary>
    public static string LayoutUndo => Get();
    /// <summary>Rétablit la modification annulée.</summary>
    public static string LayoutRedo => Get();
    /// <summary>{0} : le nom du module qui gêne.</summary>
    public static string LayoutBlocked => Get();
    /// <summary>Le cadre sort du damier.</summary>
    public static string LayoutOutside => Get();
    /// <summary>{0} : le nom du module ; {1} × {2} : colonnes × rangées minimum.</summary>
    public static string LayoutBelowMinimum => Get();
    /// <summary>Ajout impossible.</summary>
    public static string LayoutNoRoom => Get();
    /// <summary>Le service ne connaît pas ce type.</summary>
    public static string LayoutUnknownType => Get();
    /// <summary>{0} × {1} : colonnes × rangées du module choisi.</summary>
    public static string LayoutCells => Get();
    /// <summary>{0} × {1} : largeur × hauteur en pixels du module choisi.</summary>
    public static string LayoutPixels => Get();
    /// <summary>{0} × {1} : colonnes × rangées minimum du type.</summary>
    public static string LayoutMinimum => Get();
    /// <summary>Taille d'un module dans la liste : colonnes × rangées.</summary>
    public static string LayoutSize => Get();
    /// <summary>Titre des boutons de préréglage.</summary>
    public static string LayoutPresets => Get();
    /// <summary>Compact</summary>
    public static string LayoutPresetCompact => Get();
    /// <summary>Idéal</summary>
    public static string LayoutPresetIdeal => Get();
    /// <summary>Large</summary>
    public static string LayoutPresetLarge => Get();
    /// <summary>Titre des boutons de taille de texte.</summary>
    public static string LayoutTextSize => Get();
    /// <summary>Petit</summary>
    public static string LayoutTextSmall => Get();
    /// <summary>Normal</summary>
    public static string LayoutTextNormal => Get();
    /// <summary>Grand</summary>
    public static string LayoutTextLarge => Get();
    /// <summary>Retire le module choisi de l'écran (il reste dans « Retirés »).</summary>
    public static string LayoutRemove => Get();
    /// <summary>Colonne de droite sans module choisi.</summary>
    public static string LayoutNothingSelected => Get();
    /// <summary>Liste des modules placés vide.</summary>
    public static string LayoutEmpty => Get();
    /// <summary>Point d'état d'un module.</summary>
    public static string LayoutHealthOnline => Get();
    /// <summary>Point d'état d'un module.</summary>
    public static string LayoutHealthIdle => Get();
    /// <summary>Point d'état d'un module.</summary>
    public static string LayoutHealthOffline => Get();
    /// <summary>Nom accessible du damier posé sur la bande.</summary>
    public static string LayoutGridName => Get();
}
