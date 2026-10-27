namespace Trofeo.Settings.Services;

/// <summary>
/// Ce que l'onglet Paramètres demande à Windows : le démarrage avec la session, le lancement en administrateur, la
/// recherche des programmes voisins, la mémoire de la fenêtre. Réuni pour que les tests et les aperçus en donnent des faux.
/// </summary>
/// <param name="LocateDisplay">Où est Trofeo.Display.exe ; <c>null</c> : <c>AppLocator</c>. Le délégué rend <c>null</c> s'il est introuvable.</param>
/// <param name="LocateHelper">Où est Trofeo.SensorHelper.exe ; <c>null</c> : <c>AppLocator</c>.</param>
/// <param name="OwnMemory">La mémoire privée de la fenêtre elle-même, en octets ; <c>null</c> : celle du processus.</param>
public sealed record SystemServices(
    IStartupRegistration Startup, IElevatedLauncher Launcher,
    Func<string?>? LocateDisplay = null, Func<string?>? LocateHelper = null, Func<long>? OwnMemory = null)
{
    /// <summary>Rien n'est inscrit ni lancé (tests, aperçus).</summary>
    public static SystemServices None => new(new NoStartupRegistration(), new NoElevatedLauncher());

    /// <summary>Le registre et <c>runas</c> de Windows.</summary>
    public static SystemServices Windows() => new(RunKeyStartup.Create(), RunasLauncher.Create());
}
