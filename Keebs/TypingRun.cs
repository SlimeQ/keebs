namespace Keebs;

internal sealed record TypingRun(
    DateTimeOffset Timestamp,
    string Prompt,
    string TypedText,
    double ElapsedSeconds,
    double WordsPerMinute,
    double Accuracy,
    int EditDistance);
