using System.Diagnostics;

namespace Trofeo.Display.Tests;

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
}
