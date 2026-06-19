namespace Keebs;

internal sealed record SwipeTraceEvent(
    DateTimeOffset Timestamp,
    string TracedLetters,
    string? Suggestion,
    string? OutputText,
    bool Committed,
    string CurrentWord,
    IReadOnlyList<string> PreviousWords,
    IReadOnlyList<SwipeTraceCandidate> Candidates);

internal sealed record SwipeTraceCandidate(string Text, double? Score);
