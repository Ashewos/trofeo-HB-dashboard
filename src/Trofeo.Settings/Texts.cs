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
    /// <summary>La disposition a changé en dehors des réglages : elle a été relue.</summary>
    public static string LayoutChangedOutside => Get();
    /// <summary>Le service refuse sans donner de motif.</summary>
    public static string ReloadRefused => Get();
    /// <summary>{0} : la cause.</summary>
    public static string SaveFailed => Get();
}
