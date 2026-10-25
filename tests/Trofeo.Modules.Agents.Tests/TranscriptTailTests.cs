using System.Text;

namespace Trofeo.Modules.Agents.Tests;

public sealed class TranscriptTailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-tail-" + Guid.NewGuid().ToString("N"));

    public TranscriptTailTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath() => Path.Combine(_dir, "t.jsonl");

    private static void Append(string path, string text) => Append(path, Encoding.UTF8.GetBytes(text));

    private static void Append(string path, byte[] bytes)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        fs.Write(bytes);
    }

    [Fact]
    public void Tail_FirstCall_ReturnsExistingLines()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n{\"a\":2}\n");
        var tail = new TranscriptTail(path);

        var lines = tail.ReadNewLines();

        Assert.Equal(["{\"a\":1}", "{\"a\":2}"], lines);
        Assert.True(tail.Restarted);
    }

    [Fact]
    public void Tail_Appended_ReturnsOnlyNewLines()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n");
        var tail = new TranscriptTail(path);
        tail.ReadNewLines();

        Append(path, "{\"a\":2}\r\n");
        var lines = tail.ReadNewLines();

        Assert.Equal(["{\"a\":2}"], lines);
        Assert.False(tail.Restarted);
    }

    [Fact]
    public void Tail_NothingNew_ReturnsEmpty()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n");
        var tail = new TranscriptTail(path);
        tail.ReadNewLines();

        Assert.Empty(tail.ReadNewLines());
        Assert.False(tail.Restarted);
    }

    [Fact]
    public void Tail_PartialLine_WaitsForNewline()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}");
        var tail = new TranscriptTail(path);

        Assert.Empty(tail.ReadNewLines());

        Append(path, "\n");
        Assert.Equal(["{\"a\":1}"], tail.ReadNewLines());
    }

    [Fact]
    public void Tail_MultiByteCharacterSplitBetweenCalls_NoReplacementCharacter()
    {
        var path = FilePath();
        var bytes = Encoding.UTF8.GetBytes("{\"t\":\"é€😀\"}\n");
        var cut = Array.IndexOf(bytes, (byte)0xC3) + 1; // au milieu du « é »
        Append(path, bytes[..cut]);
        var tail = new TranscriptTail(path);
        Assert.Empty(tail.ReadNewLines());

        Append(path, bytes[cut..]);
        var lines = tail.ReadNewLines();

        Assert.Equal(["{\"t\":\"é€😀\"}"], lines);
    }

    [Fact]
    public void Tail_LargeFile_ReadsOnlyLastChunk()
    {
        var path = FilePath();
        const int count = 220_000;
        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            writer.NewLine = "\n";
            for (var i = 0; i < count; i++)
            {
                writer.WriteLine($"{{\"n\":{i,8}}}"); // 14 octets + \n : ~3 Mio
            }
        }

        Assert.True(new FileInfo(path).Length > 3 * 1024 * 1024);
        var tail = new TranscriptTail(path, initialBytes: 1024);

        var lines = tail.ReadNewLines();

        Assert.True(tail.Restarted);
        Assert.InRange(lines.Count, 50, 80);
        Assert.All(lines, l => Assert.Matches(@"^\{""n"":\s*\d+\}$", l)); // aucune ligne coupée
        Assert.Equal($"{{\"n\":{count - 1,8}}}", lines[^1]);
    }

    [Fact]
    public void Tail_LargeFile_ChunkStartingOnLineBoundary_KeepsFirstLine()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n{\"a\":2}\n{\"a\":3}\n"); // 24 octets, lignes de 8
        var tail = new TranscriptTail(path, initialBytes: 16);

        Assert.Equal(["{\"a\":2}", "{\"a\":3}"], tail.ReadNewLines());
    }

    [Fact]
    public void Tail_ReadCappedPerCall_RestComesNextCall()
    {
        var path = FilePath();
        const int count = 400_000;
        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            writer.NewLine = "\n";
            for (var i = 0; i < count; i++)
            {
                writer.WriteLine($"{{\"n\":{i,8}}}"); // ~6 Mio
            }
        }

        var tail = new TranscriptTail(path, initialBytes: 16 * 1024 * 1024);

        var first = tail.ReadNewLines();
        var second = tail.ReadNewLines();

        Assert.True(first.Count < count);
        Assert.True(second.Count > 0);
        Assert.Equal(count, first.Count + second.Count);
        Assert.Equal($"{{\"n\":{0,8}}}", first[0]);
        Assert.Equal($"{{\"n\":{count - 1,8}}}", second[^1]);
        Assert.False(tail.Restarted);
    }

    [Fact]
    public void Tail_Truncated_Restarts()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n{\"a\":2}\n{\"a\":3}\n");
        var tail = new TranscriptTail(path);
        tail.ReadNewLines();

        File.WriteAllText(path, "{\"b\":1}\n");
        var lines = tail.ReadNewLines();

        Assert.Equal(["{\"b\":1}"], lines);
        Assert.True(tail.Restarted);
    }

    [Fact]
    public void Tail_OversizedLine_Skipped()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n" + new string('x', 500) + "\n{\"a\":2}\n");
        var tail = new TranscriptTail(path, maxLineBytes: 100);

        Assert.Equal(["{\"a\":1}", "{\"a\":2}"], tail.ReadNewLines());
    }

    [Fact]
    public void Tail_OversizedLineSpanningCalls_SkippedUntilNewline()
    {
        var path = FilePath();
        Append(path, "{\"a\":1}\n" + new string('x', 150));
        var tail = new TranscriptTail(path, maxLineBytes: 100);
        Assert.Equal(["{\"a\":1}"], tail.ReadNewLines());

        Append(path, new string('y', 150));
        Assert.Empty(tail.ReadNewLines());

        Append(path, "zzz\n{\"a\":2}\n");
        Assert.Equal(["{\"a\":2}"], tail.ReadNewLines());
    }

    [Fact]
    public void Tail_InvalidUtf8_ReplacementCharacter()
    {
        var path = FilePath();
        Append(path, [(byte)'a', 0xFF, (byte)'b', (byte)'\n']);
        var tail = new TranscriptTail(path);

        Assert.Equal(["a�b"], tail.ReadNewLines());
    }

    [Fact]
    public void Tail_MissingFile_Empty()
    {
        var tail = new TranscriptTail(FilePath());

        Assert.Empty(tail.ReadNewLines());
        Assert.False(tail.Restarted);
    }

    [Fact]
    public void Tail_FileCreatedAfterFirstCall_ReadsFromStart()
    {
        var path = FilePath();
        var tail = new TranscriptTail(path);
        Assert.Empty(tail.ReadNewLines());

        Append(path, "{\"a\":1}\n");

        Assert.Equal(["{\"a\":1}"], tail.ReadNewLines());
        Assert.True(tail.Restarted);
    }

    [Fact]
    public void Tail_FileLockedForWriting_StillReads()
    {
        var path = FilePath();
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("{\"a\":1}\n"u8);
        writer.Flush();
        var tail = new TranscriptTail(path);

        Assert.Equal(["{\"a\":1}"], tail.ReadNewLines());

        writer.Write("{\"a\":2}\n"u8);
        writer.Flush();
        Assert.Equal(["{\"a\":2}"], tail.ReadNewLines());
    }
}
