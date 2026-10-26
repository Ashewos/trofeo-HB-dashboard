using System.ComponentModel;
using System.Diagnostics;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents;

/// <summary>
/// Installe le mod <c>trofeo-quotas</c> dans Claude Code : ajoute le dossier <c>mods</c> comme marché de modules, puis installe
/// le mod. <paramref name="run"/> lance <c>claude</c> avec les arguments donnés et rend le code de retour et la sortie ;
/// elle lève <see cref="Win32Exception"/> (ou <see cref="FileNotFoundException"/>) quand <c>claude</c> est introuvable.
/// </summary>
public sealed class QuotaModInstaller(
    Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> run, string? modsDir)
{
    internal const string PluginName = "trofeo-quotas@trofeo-hb-dashboard";
    private const int MaxOutputLength = 300;

    public async Task<ActionResult> InstallAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(modsDir) || !Directory.Exists(modsDir))
            return new ActionResult(false, "Le dossier du mod est introuvable.");

        try
        {
            foreach (var arguments in new[] { $"plugin marketplace add \"{modsDir}\"", $"plugin install {PluginName}" })
            {
                var (exitCode, output) = await run("claude", arguments, ct);
                if (exitCode != 0)
                {
                    var text = output.Trim();
                    if (text.Length > MaxOutputLength) text = text[..MaxOutputLength];
                    return new ActionResult(false, "L'installation a échoué : " + text);
                }
            }
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException)
        {
            return new ActionResult(false, "Claude Code est introuvable sur ce PC.");
        }

        return new ActionResult(true, "Mod installé : il sera chargé par les nouvelles sessions de Claude Code.");
    }

    /// <summary>Lanceur réel : <c>claude</c> est résolu par le <c>PATH</c> ; 60 s au plus par commande.</summary>
    internal static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        process.Start();

        // Lecture des deux flux en parallèle : un tampon plein bloquerait la commande.
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Le processus s'est terminé entre-temps : rien à arrêter.
            }

            ct.ThrowIfCancellationRequested();
            return (-1, "La commande a dépassé 60 s.");
        }

        return (process.ExitCode, await stdout + await stderr);
    }
}
