using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Trofeo.Sensors;

/// <summary>
/// Identité de l'assistant de capteurs : le client n'accepte un tube que si son serveur est un exécutable
/// installé dans le dossier de l'assistant (écrit uniquement par un administrateur), jamais un processus
/// quelconque qui aurait créé le tube avant l'assistant.
/// </summary>
public static partial class HelperIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaxPathChars = 32768;

    /// <summary><c>%ProgramFiles%\trofeo-HB-dashboard\sensors\</c>, séparateur final compris (« sensors-evil » ne doit pas correspondre).</summary>
    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "trofeo-HB-dashboard", "sensors")
        + Path.DirectorySeparatorChar;

    /// <summary>Vrai si l'exécutable du processus est sous <see cref="InstallDir"/>. Toute erreur (processus disparu, accès refusé) donne faux.</summary>
    public static bool IsTrusted(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
            if (process.IsInvalid) return false;

            var buffer = new char[MaxPathChars];
            var size = (uint)buffer.Length;
            bool ok;
            unsafe
            {
                fixed (char* name = buffer) ok = QueryFullProcessImageNameW(process, 0, name, ref size);
            }
            return ok && IsTrustedPath(new string(buffer, 0, (int)size));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.TraceWarning($"Vérification de l'assistant de capteurs impossible : {ex.Message}");
            return false;
        }
    }

    /// <summary>Règle de préfixe : chemin absolu normalisé (« .. » résolus) sous <see cref="InstallDir"/>, casse ignorée.</summary>
    internal static bool IsTrustedPath(string path)
    {
        // Un chemin relatif serait résolu depuis le dossier courant : on ne le compare jamais.
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            return Path.GetFullPath(path).StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // SafeProcessHandle : CloseHandle est appelé à la libération du handle, même en cas d'exception.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* exeName, ref uint size);
}
