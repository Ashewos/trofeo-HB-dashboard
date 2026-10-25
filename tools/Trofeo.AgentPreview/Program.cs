using System.Text;
using Trofeo.Modules.Agents;

namespace Trofeo.AgentPreview;

/// <summary>
/// Outil d'aperçu du module Claude Code / Codex. <c>--codex-probe [codexHome]</c> décrit la structure de la transcription
/// Codex la plus récente (les clés et leur genre, jamais les valeurs) ; les autres options viennent avec le dessin du module.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage : Trofeo.AgentPreview <option>
          --codex-probe [codexHome]   décrit la structure (clés, sans valeurs) de la transcription Codex la plus récente
        """;

    private static int Main(string[] args)
    {
        if (args.Length is 1 or 2 && args[0] == "--codex-probe")
        {
            return CodexProbeCommand(args.Length == 2 ? args[1] : CodexHome.Resolve());
        }

        Console.WriteLine(Usage);
        return args.Length == 0 ? 0 : 2;
    }

    private static int CodexProbeCommand(string codexHome)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        FileInfo? latest = null;
        try
        {
            latest = Directory.Exists(sessions)
                ? new DirectoryInfo(sessions).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Impossible de parcourir " + sessions + " : " + e.Message);
            return 1;
        }

        if (latest is null)
        {
            Console.Error.WriteLine("Aucune transcription Codex trouvée dans " + sessions);
            return 1;
        }

        try
        {
            Console.WriteLine("Transcription : " + latest.Name);
            Console.WriteLine(CodexProbe.Describe(ReadLines(latest.FullName)));
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Impossible de lire " + latest.Name + " : " + e.Message);
            return 1;
        }
    }

    /// <summary>Les lignes du fichier, ouvert en partage complet : Codex peut être en train d'y écrire.</summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
