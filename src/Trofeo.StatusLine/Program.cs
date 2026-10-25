namespace Trofeo.StatusLine;

/// <summary>
/// Commande de barre d'état de Claude Code : lit le JSON sur l'entrée standard et en garde une capture
/// pour le tableau de bord. Elle est lancée à chaque mise à jour de la barre : elle démarre vite (bibliothèque
/// standard seule), n'écrit jamais sur la sortie d'erreur et rend toujours 0, pour ne jamais gêner Claude Code.
/// Avec <c>--tee</c>, elle recopie ensuite l'entrée telle quelle sur la sortie standard, pour une commande chaînée.
/// </summary>
internal static class Program
{
    /// <summary>Taille maximale lue sur l'entrée standard (1 Mio).</summary>
    internal const int MaxInputBytes = 1024 * 1024;

    private static int Main(string[] args)
    {
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            return Run(args, stdin, stdout, ClaudeHome.StatusLineDir(ClaudeHome.Resolve()), DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Corps de la commande, avec flux et dossier injectés pour les tests. Rend toujours 0.</summary>
    internal static int Run(string[] args, Stream stdin, Stream stdout, string outputDir, DateTimeOffset now)
    {
        try
        {
            var input = ReadLimited(stdin);

            try
            {
                if (StatusLineCapture.Parse(input, now) is { } record) StatusLineFile.Write(outputDir, record);
            }
            catch (Exception)
            {
                // Une capture manquée ne doit jamais se voir.
            }

            if (Array.IndexOf(args, "--tee") >= 0)
            {
                stdout.Write(input);
                stdout.Flush();
            }
        }
        catch (Exception)
        {
            // Entrée ou sortie fermée : rien à signaler.
        }
        return 0;
    }

    private static byte[] ReadLimited(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (buffer.Length < MaxInputBytes)
        {
            var read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, MaxInputBytes - buffer.Length));
            if (read <= 0) break;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
