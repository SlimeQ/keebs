namespace Keebs;

internal sealed record PredictionContext(string CurrentWord, IReadOnlyList<string> PreviousWords)
{
    public string PreviousWord => PreviousWords.Count == 0 ? string.Empty : PreviousWords[^1];
}
