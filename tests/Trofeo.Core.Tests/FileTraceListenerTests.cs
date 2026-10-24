using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Trofeo.Core.Logging;

namespace Trofeo.Core.Tests;

public sealed class FileTraceListenerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-log-" + Guid.NewGuid().ToString("N"));

    public FileTraceListenerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string LogPath => Path.Combine(_dir, "display.log");

    [Fact]
    public void WriteLine_ExceedsMaxBytes_RotatesToSingleBackup()
    {
        using (var listener = new FileTraceListener(LogPath, maxBytes: 500))
        {
            for (var i = 0; i < 100; i++)
                listener.WriteLine($"ligne {i:000}");
        }

        Assert.True(File.Exists(LogPath + ".1"));
        Assert.False(File.Exists(LogPath + ".2"));
        Assert.InRange(new FileInfo(LogPath).Length, 1, 500 + 100); // le courant reste borné (plafond + une ligne)
        Assert.InRange(new FileInfo(LogPath + ".1").Length, 1, 500 + 100);
        Assert.Contains("ligne 099", File.ReadAllText(LogPath)); // les dernières lignes sont dans le courant
    }

    [Fact]
    public void Constructor_ExistingOversizedFile_IsRotatedAtStartup()
    {
        File.WriteAllText(LogPath, new string('a', 2000));

        using (var listener = new FileTraceListener(LogPath, maxBytes: 500))
            listener.WriteLine("neuf");

        Assert.Equal(2000, new FileInfo(LogPath + ".1").Length);
        Assert.Contains("neuf", File.ReadAllText(LogPath));
        Assert.DoesNotContain("aaaa", File.ReadAllText(LogPath));
    }

    [Fact]
    public void TraceEvent_WritesLevelAndMessage()
    {
        using (var listener = new FileTraceListener(LogPath))
            listener.TraceEvent(null, "src", TraceEventType.Error, 0, "boum");

        Assert.Contains("[Erreur] boum", File.ReadAllText(LogPath));
    }

    [Fact]
    public void Write_ReopenFails_RecoversOnLaterWrite()
    {
        var time = new FakeTimeProvider();
        var opens = 0;
        Stream Open(string path)
        {
            // 1re ouverture : constructeur ; 2e : réouverture après la rotation, qui échoue ; ensuite normal.
            if (++opens == 2) throw new IOException("réouverture impossible");
            return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        }

        using (var listener = new FileTraceListener(LogPath, maxBytes: 200, time, Open))
        {
            listener.WriteLine(new string('x', 250)); // dépasse le plafond
            listener.WriteLine("perdue 1"); // rotation, réouverture en échec
            listener.WriteLine("perdue 2"); // aucune exception, pas de nouvel essai avant 60 s
            time.Advance(TimeSpan.FromSeconds(59));
            listener.WriteLine("perdue 3");
            Assert.Equal(2, opens);

            time.Advance(TimeSpan.FromSeconds(1));
            listener.WriteLine("revenue");
        }

        Assert.Equal(3, opens);
        var text = File.ReadAllText(LogPath);
        Assert.Contains("revenue", text);
        Assert.DoesNotContain("perdue", text);
        Assert.True(File.Exists(LogPath + ".1"));
    }
}
