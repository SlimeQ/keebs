using System.Text.Json;

namespace Keebs.Tests;

public sealed class TypingRunTests
{
    [Fact]
    public void ComputesTypingRunMetrics()
    {
        var run = TypingRunMetrics.CreateRun(
            "hello world",
            "hello wurld",
            TimeSpan.FromSeconds(30),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(4.4, run.WordsPerMinute, precision: 1);
        Assert.Equal(1, run.EditDistance);
        Assert.InRange(run.Accuracy, 0.90, 0.92);
    }

    [Fact]
    public void RecorderAppendsJsonLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "Keebs.Tests", $"{Guid.NewGuid():N}", "typing-runs.jsonl");
        var recorder = new TypingRunRecorder(path);
        var run = TypingRunMetrics.CreateRun(
            "short prompt",
            "short prompt",
            TimeSpan.FromSeconds(12),
            DateTimeOffset.UnixEpoch);

        recorder.Append(run);
        recorder.Append(run);

        var lines = File.ReadAllLines(path);
        var parsed = JsonSerializer.Deserialize<TypingRun>(lines[0]);

        Assert.Equal(2, lines.Length);
        Assert.NotNull(parsed);
        Assert.Equal("short prompt", parsed!.Prompt);
        Assert.Equal(1, parsed.Accuracy);
    }
}
