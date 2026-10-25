using System.Text;
using Trofeo.StatusLine;

namespace Trofeo.Modules.Agents.Tests;

public sealed class StatusLineCaptureTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-sl-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private const string FullPayload = """
        {
          "session_id": "3f2a-9b",
          "transcript_path": "/home/a/.claude/projects/x/3f2a-9b.jsonl",
          "cwd": "/home/a/x",
          "model": {"id": "claude-opus-5-5", "display_name": "Opus 5.5"},
          "context_window": {
            "total_input_tokens": 1234, "total_output_tokens": 567, "context_window_size": 200000,
            "current_usage": {"input_tokens": 1}, "used_percentage": 42.5, "remaining_percentage": 57.5
          },
          "rate_limits": {
            "five_hour": {"used_percentage": 31, "resets_at": 1792940400},
            "seven_day": {"used_percentage": 12, "resets_at": "2026-10-27T08:00:00Z"}
          }
        }
        """;

    private static StatusLineRecord Sample(string id, DateTimeOffset at) => new(
        at, id, "claude-opus-5-5", 200000, 42.5,
        new CapturedQuota(31, new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero)),
        null);

    [Fact]
    public void Parse_FullPayload()
    {
        var record = StatusLineCapture.Parse(Encoding.UTF8.GetBytes(FullPayload), Now);

        Assert.NotNull(record);
        Assert.Equal(Now, record.CapturedAt);
        Assert.Equal("3f2a-9b", record.SessionId);
        Assert.Equal("claude-opus-5-5", record.ModelId);
        Assert.Equal(200000, record.ContextWindowSize);
        Assert.Equal(42.5, record.ContextUsedPercent);
        Assert.NotNull(record.FiveHour);
        Assert.Equal(31, record.FiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792940400), record.FiveHour.ResetsAt);
        Assert.NotNull(record.SevenDay);
        Assert.Equal(12, record.SevenDay.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero), record.SevenDay.ResetsAt);
    }

    [Fact]
    public void Parse_NoRateLimits_QuotasNull()
    {
        var record = StatusLineCapture.Parse(
            """{"session_id":"abc","model":{"id":"m"},"context_window":{"context_window_size":"grand","used_percentage":10}}"""u8,
            Now);

        Assert.NotNull(record);
        Assert.Null(record.FiveHour);
        Assert.Null(record.SevenDay);
        Assert.Equal("m", record.ModelId);
        Assert.Null(record.ContextWindowSize); // d'un autre type : null, sans erreur
        Assert.Equal(10, record.ContextUsedPercent);
    }

    [Fact]
    public void Parse_QuotaWithoutPercentage_Null()
    {
        var record = StatusLineCapture.Parse(
            """{"session_id":"abc","rate_limits":{"five_hour":{"resets_at":1792940400},"seven_day":"x"}}"""u8, Now);

        Assert.NotNull(record);
        Assert.Null(record.FiveHour);
        Assert.Null(record.SevenDay);
    }

    [Theory]
    [InlineData("""{"session_id":"../x"}""")]
    [InlineData("""{"session_id":""}""")]
    [InlineData("""{"session_id":12}""")]
    [InlineData("""{"model":{"id":"m"}}""")]
    public void Parse_BadSessionId_Null(string json)
    {
        Assert.Null(StatusLineCapture.Parse(Encoding.UTF8.GetBytes(json), Now));
    }

    [Fact]
    public void Parse_SessionIdTooLong_Null()
    {
        var json = $$"""{"session_id":"{{new string('a', 65)}}"}""";
        Assert.Null(StatusLineCapture.Parse(Encoding.UTF8.GetBytes(json), Now));
        var ok = $$"""{"session_id":"{{new string('a', 64)}}"}""";
        Assert.NotNull(StatusLineCapture.Parse(Encoding.UTF8.GetBytes(ok), Now));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("{\"session_id\":\"a\"")]
    public void Parse_Garbage_Null(string json)
    {
        Assert.Null(StatusLineCapture.Parse(Encoding.UTF8.GetBytes(json), Now));
    }

    [Fact]
    public void File_WriteThenReadAll_RoundTrips()
    {
        var record = Sample("s-1", Now);
        StatusLineFile.Write(_dir, record);

        Assert.True(File.Exists(Path.Combine(_dir, "s-1.json")));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        var all = StatusLineFile.ReadAll(_dir, Now, TimeSpan.FromHours(1));
        Assert.Equal([record], all);
    }

    [Fact]
    public void File_Write_IsCamelCaseWithExplicitNulls()
    {
        StatusLineFile.Write(_dir, Sample("s-1", Now));

        var text = File.ReadAllText(Path.Combine(_dir, "s-1.json"));
        Assert.Contains("\"sessionId\":\"s-1\"", text);
        Assert.Contains("\"fiveHour\":{\"usedPercent\":31", text);
        Assert.Contains("\"sevenDay\":null", text);
    }

    [Fact]
    public void File_Write_ReplacesPreviousCapture()
    {
        StatusLineFile.Write(_dir, Sample("s-1", Now.AddMinutes(-5)));
        var newer = Sample("s-1", Now);
        StatusLineFile.Write(_dir, newer);

        Assert.Equal([newer], StatusLineFile.ReadAll(_dir, Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void File_Write_RejectsUnsafeSessionId()
    {
        Assert.ThrowsAny<ArgumentException>(() => StatusLineFile.Write(_dir, Sample("../evil", Now)));
    }

    [Fact]
    public void File_ReadAll_MissingDirectory_Empty()
    {
        Assert.Empty(StatusLineFile.ReadAll(Path.Combine(_dir, "absent"), Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void File_ReadAll_SkipsOldAndCorrupt()
    {
        StatusLineFile.Write(_dir, Sample("recent", Now.AddMinutes(-10)));
        StatusLineFile.Write(_dir, Sample("old", Now.AddHours(-3)));
        File.WriteAllText(Path.Combine(_dir, "corrupt.json"), "{pas du json");
        File.WriteAllText(Path.Combine(_dir, "empty.json"), "{}");

        var all = StatusLineFile.ReadAll(_dir, Now, TimeSpan.FromHours(1));

        Assert.Equal(["recent"], all.Select(r => r.SessionId));
    }

    [Fact]
    public void File_DeleteOlderThan()
    {
        StatusLineFile.Write(_dir, Sample("recent", Now.AddMinutes(-10)));
        StatusLineFile.Write(_dir, Sample("old", Now.AddDays(-9)));

        StatusLineFile.DeleteOlderThan(_dir, Now, TimeSpan.FromDays(7));

        Assert.True(File.Exists(Path.Combine(_dir, "recent.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "old.json")));
    }

    [Fact]
    public void Program_Tee_EchoesInputAndWritesCapture()
    {
        var input = Encoding.UTF8.GetBytes(FullPayload);
        using var stdin = new MemoryStream(input);
        using var stdout = new MemoryStream();

        var code = Program.Run(["--tee"], stdin, stdout, _dir, Now);

        Assert.Equal(0, code);
        Assert.Equal(input, stdout.ToArray());
        var all = StatusLineFile.ReadAll(_dir, Now, TimeSpan.FromHours(1));
        Assert.Equal("3f2a-9b", Assert.Single(all).SessionId);
    }

    [Fact]
    public void Program_WithoutTee_WritesNothingOnStdout()
    {
        using var stdin = new MemoryStream(Encoding.UTF8.GetBytes(FullPayload));
        using var stdout = new MemoryStream();

        var code = Program.Run([], stdin, stdout, _dir, Now);

        Assert.Equal(0, code);
        Assert.Equal(0, stdout.Length);
        Assert.Single(StatusLineFile.ReadAll(_dir, Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Program_GarbageInput_ExitsZeroSilently()
    {
        using var stdin = new MemoryStream("pas du json"u8.ToArray());
        using var stdout = new MemoryStream();

        var code = Program.Run([], stdin, stdout, _dir, Now);

        Assert.Equal(0, code);
        Assert.Equal(0, stdout.Length);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Program_UnwritableDirectory_StillExitsZeroAndTees()
    {
        // Un fichier à la place du dossier : l'écriture échoue, la barre d'état de Claude Code ne doit pas le voir.
        var blocker = Path.Combine(Path.GetTempPath(), "trofeo-sl-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "x");
        try
        {
            var input = Encoding.UTF8.GetBytes(FullPayload);
            using var stdin = new MemoryStream(input);
            using var stdout = new MemoryStream();

            var code = Program.Run(["--tee"], stdin, stdout, Path.Combine(blocker, "sous"), Now);

            Assert.Equal(0, code);
            Assert.Equal(input, stdout.ToArray());
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void Program_InputOverOneMebibyte_IsTruncated()
    {
        var big = new byte[Program.MaxInputBytes + 5000];
        Array.Fill(big, (byte)'x');
        using var stdin = new MemoryStream(big);
        using var stdout = new MemoryStream();

        var code = Program.Run(["--tee"], stdin, stdout, _dir, Now);

        Assert.Equal(0, code);
        Assert.Equal(Program.MaxInputBytes, stdout.Length);
    }
}
