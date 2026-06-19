using System.Text.Json;

namespace Keebs.Tests;

public sealed class SwipeTraceRecorderTests
{
    [Fact]
    public void RecorderAppendsSwipeTraceJsonLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "Keebs.Tests", $"{Guid.NewGuid():N}", "swipe-traces.jsonl");
        var recorder = new SwipeTraceRecorder(path);
        var trace = new SwipeTraceEvent(
            DateTimeOffset.UnixEpoch,
            "iuds",
            "is",
            "is",
            true,
            string.Empty,
            [],
            [new SwipeTraceCandidate("is", 0.12)]);

        recorder.Append(trace);

        var line = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize<SwipeTraceEvent>(line);

        Assert.NotNull(parsed);
        Assert.Equal("iuds", parsed!.TracedLetters);
        Assert.Equal("is", parsed.Suggestion);
        Assert.True(parsed.Committed);
        Assert.Equal("is", parsed.Candidates[0].Text);
    }
}
