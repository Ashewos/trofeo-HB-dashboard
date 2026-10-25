using Microsoft.Win32;

namespace Trofeo.Sensors;

/// <summary>Dit si le pilote PawnIO, dont LibreHardwareMonitor a besoin pour lire les capteurs de la carte mère, est installé.</summary>
public interface IPawnIoDetector
{
    bool IsInstalled();
}

/// <summary>PawnIO est installé quand sa clé de désinstallation existe dans le registre 64 bits.</summary>
public sealed class PawnIoDetector : IPawnIoDetector
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public bool IsInstalled()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(UninstallKey);
        return key is not null;
    }
}
