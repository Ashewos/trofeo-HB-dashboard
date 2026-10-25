using System.Text;

namespace Trofeo.Modules.Agents;

/// <summary>Retrouve les derniers messages/outils avant une grosse compression, uniquement au démarrage d'un fil vide.</summary>
internal static class CodexConversationHistory
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    private const int MaxLineBytes = 1024 * 1024;

    public static CodexRollout? Read(string path, long end)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Ne pas lire les ajouts apparus après le relevé : le lecteur incrémental les appliquera plus tard.
            if (end <= 0 || end > stream.Length) { return null; }
            var start = Math.Max(0, end - MaxBytes);
            var discarding = false;
            if (start > 0)
            {
                stream.Seek(start - 1, SeekOrigin.Begin);
                discarding = stream.ReadByte() != '\n';
            }
            else { stream.Seek(0, SeekOrigin.Begin); }

            var rollout = new CodexRollout();
            using var pending = new MemoryStream();
            var buffer = new byte[64 * 1024];
            var remaining = end - start;
            while (remaining > 0)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) { return null; } // troncature pendant la lecture
                remaining -= read;
                var chunk = buffer.AsSpan(0, read);
                while (!chunk.IsEmpty)
                {
                    var newline = chunk.IndexOf((byte)'\n');
                    var part = newline < 0 ? chunk : chunk[..newline];
                    if (!discarding)
                    {
                        if (pending.Length + part.Length > MaxLineBytes)
                        {
                            pending.SetLength(0);
                            discarding = true;
                        }
                        else { pending.Write(part); }
                    }

                    if (newline < 0) { break; }
                    if (!discarding)
                    {
                        // CodexRollout n'expose ni compacted, ni raisonnement, ni instructions injectées.
                        rollout.Apply(Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length));
                    }
                    pending.SetLength(0);
                    discarding = false;
                    chunk = chunk[(newline + 1)..];
                }
            }

            // Une dernière ligne sans fin de ligne reste au lecteur incrémental, jamais appliquée deux fois.
            return rollout;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
