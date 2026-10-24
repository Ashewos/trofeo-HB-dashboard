using System.Text;

namespace Trofeo.Sensors;

/// <summary>Résultat de <see cref="HelperInstaller.Install"/>.</summary>
public enum InstallResult
{
    /// <summary>Assistant copié, tâche créée et démarrée.</summary>
    Installed,

    /// <summary>Comme <see cref="Installed"/>, mais l'installeur de PawnIO demande un redémarrage pour finir.</summary>
    InstalledRebootRequired,

    /// <summary>PawnIO n'est pas installé et aucun installeur n'a été fourni : rien n'a été fait.</summary>
    PawnIoMissing,

    /// <summary>L'installeur de PawnIO a échoué : rien d'autre n'a été fait.</summary>
    PawnIoFailed,
}

/// <summary>
/// Installe l'assistant de capteurs (PawnIO, copie dans <paramref name="installDir"/>, tâche planifiée SYSTEM) et le
/// désinstalle. Doit tourner élevé ; toutes les commandes passent par <paramref name="runner"/>.
/// </summary>
/// <param name="sourceDir">Dossier d'où copier l'assistant (celui de l'exécutable en cours).</param>
/// <param name="installDir">Dossier d'installation (voir <see cref="HelperIdentity.InstallDir"/>).</param>
public sealed class HelperInstaller(ICommandRunner runner, IPawnIoDetector pawnIo, string sourceDir, string installDir)
{
    private const string ExeName = "Trofeo.SensorHelper.exe";
    private const int PawnIoRebootRequired = 3010;
    private static readonly string QuotedTask = "\"" + TaskDefinition.TaskName + "\"";

    /// <param name="pawnIoSetupPath">Installeur de PawnIO, utilisé seulement si PawnIO est absent.</param>
    public InstallResult Install(string? pawnIoSetupPath)
    {
        var rebootRequired = false;
        if (!pawnIo.IsInstalled())
        {
            if (string.IsNullOrWhiteSpace(pawnIoSetupPath)) return InstallResult.PawnIoMissing;
            switch (runner.Run(pawnIoSetupPath, "-install -silent"))
            {
                case 0: break;
                case PawnIoRebootRequired: rebootRequired = true; break;
                default: return InstallResult.PawnIoFailed;
            }
        }

        runner.Run("schtasks", $"/End /TN {QuotedTask}"); // ancienne version éventuelle ; absente ou arrêtée : sans importance

        if (!SameDirectory(sourceDir, installDir)) CopyDirectory(sourceDir, installDir);

        var xmlPath = Path.Combine(Path.GetTempPath(), $"trofeo-sensors-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, TaskDefinition.Xml(Path.Combine(installDir, ExeName)), Encoding.Unicode); // UTF-16 avec BOM
            Check(runner.Run("schtasks", $"/Create /TN {QuotedTask} /XML \"{xmlPath}\" /F"), "création de la tâche planifiée");
        }
        finally
        {
            File.Delete(xmlPath);
        }

        Check(runner.Run("schtasks", $"/Run /TN {QuotedTask}"), "démarrage de la tâche planifiée");
        return rebootRequired ? InstallResult.InstalledRebootRequired : InstallResult.Installed;
    }

    /// <summary>Arrête et supprime la tâche, puis le dossier d'installation. PawnIO reste en place : d'autres logiciels peuvent s'en servir.</summary>
    public void Uninstall()
    {
        runner.Run("schtasks", $"/End /TN {QuotedTask}");
        runner.Run("schtasks", $"/Delete /TN {QuotedTask} /F");
        if (Directory.Exists(installDir)) Directory.Delete(installDir, recursive: true);
    }

    private static void Check(int exitCode, string step)
    {
        if (exitCode != 0) throw new InvalidOperationException($"Échec de la {step} (code {exitCode}).");
    }

    private static bool SameDirectory(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Copie récursive qui écrase les fichiers existants et ne supprime rien. Les fichiers créés héritent des droits du dossier de destination.</summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
