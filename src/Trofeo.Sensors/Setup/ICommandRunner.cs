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
    public int Run(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Impossible de lancer « {fileName} ».");
        process.WaitForExit();
        return process.ExitCode;
    }
}
