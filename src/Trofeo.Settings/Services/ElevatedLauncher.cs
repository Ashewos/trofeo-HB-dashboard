using System.ComponentModel;
using System.Diagnostics;

namespace Trofeo.Settings.Services;

/// <summary>
/// Lance un programme en administrateur (la fenêtre d'autorisation de Windows). Derrière une interface pour que les tests
/// ne lancent rien.
/// </summary>
public interface IElevatedLauncher
{
    /// <summary>
    /// Lance <paramref name="exe"/> avec <paramref name="arguments"/> et rend vrai dès qu'il est parti ; faux si l'utilisateur
    /// a refusé l'autorisation. Les autres échecs (programme introuvable…) lèvent <see cref="Win32Exception"/>.
    /// </summary>
    bool Launch(string exe, string arguments);
}

/// <summary>Sans Windows (tests, aperçus) : rien n'est lancé et l'autorisation est « refusée ».</summary>
public sealed class NoElevatedLauncher : IElevatedLauncher
{
    public bool Launch(string exe, string arguments) => false;
}

/// <summary>Le lancement par <c>runas</c> : Windows demande l'autorisation.</summary>
public sealed class RunasLauncher : IElevatedLauncher
{
    /// <summary>« L'opération a été annulée par l'utilisateur » (ERROR_CANCELLED).</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>Le lancement par <c>runas</c> ; ailleurs (tests sous Linux), <see cref="NoElevatedLauncher"/>.</summary>
    public static IElevatedLauncher Create() =>
        OperatingSystem.IsWindows() ? new RunasLauncher() : new NoElevatedLauncher();

    public bool Launch(string exe, string arguments)
    {
        var start = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = true,   // seul ShellExecute sait demander l'élévation
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        };

        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }
}
