using System.ComponentModel;
using System.Diagnostics;

namespace Trofeo.Sensors;

/// <summary>Lance une commande et attend sa fin ; abstrait <c>schtasks</c> et l'installeur de PawnIO pour les tests.</summary>
public interface ICommandRunner
{
    /// <summary>Lance <paramref name="fileName"/> avec <paramref name="arguments"/> (déjà au format ligne de commande) et renvoie son code de sortie.</summary>
    int Run(string fileName, string arguments);
}

/// <summary>Lance réellement le processus, sans fenêtre, et attend sa fin.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <exception cref="InvalidOperationException">Le programme n'a pas pu être lancé.</exception>
    public int Run(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Resolve(fileName, Environment.SystemDirectory), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException($"Impossible de lancer « {fileName} ».");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Impossible de lancer « {fileName} » : {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Un nom sans dossier (<c>schtasks</c>) est cherché dans le dossier système seulement : la recherche normale de
    /// Windows commence par le dossier de l'application et le dossier courant, où un programme planté s'exécuterait élevé.
    /// </summary>
    internal static string Resolve(string fileName, string systemDirectory)
    {
        if (fileName.AsSpan().IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0) return fileName;
        return Path.Combine(systemDirectory, Path.HasExtension(fileName) ? fileName : fileName + ".exe");
    }
}
