namespace Keebs;

internal sealed class TextPredictionEngine
{
    private readonly Dictionary<string, int> _personalFrequency = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string[]> NextWord = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = ["lot", "little", "few"],
        ["about"] = ["the", "that", "this"],
        ["are"] = ["you", "we", "the"],
        ["can"] = ["you", "we", "be"],
        ["could"] = ["you", "we", "be"],
        ["for"] = ["the", "this", "that"],
        ["how"] = ["about", "much", "long"],
        ["i"] = ["think", "would", "can"],
        ["if"] = ["you", "we", "it"],
        ["in"] = ["the", "this", "a"],
        ["is"] = ["the", "a", "it"],
        ["it"] = ["is", "would", "can"],
        ["keyboard"] = ["layout", "input", "prediction"],
        ["let"] = ["me", "us", "it"],
        ["of"] = ["the", "this", "that"],
        ["on"] = ["the", "windows", "screen"],
        ["that"] = ["works", "would", "is"],
        ["the"] = ["keyboard", "app", "text"],
        ["this"] = ["is", "works", "should"],
        ["to"] = ["the", "be", "use"],
        ["we"] = ["can", "should", "could"],
        ["what"] = ["about", "is", "we"],
        ["windows"] = ["keyboard", "osk", "app"],
        ["with"] = ["the", "a", "this"],
        ["would"] = ["be", "work", "need"],
        ["you"] = ["can", "should", "want"]
    };

    private static readonly string[] Vocabulary =
    [
        "accessibility", "admin", "application", "autocorrect", "available", "behavior", "browser",
        "button", "candidate", "clipboard", "completion", "context", "control", "desktop", "dictionary",
        "elevated", "field", "floating", "focus", "input", "keyboard", "layout", "learning", "local",
        "model", "native", "offline", "password", "prediction", "private", "program", "replacement",
        "secure", "sensitive", "service", "settings", "shortcut", "suggestion", "surface", "system",
        "text", "touch", "typing", "window", "windows"
    ];

    public IEnumerable<string> GetSuggestions(PredictionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.CurrentWord))
        {
            return CompleteWord(context.CurrentWord);
        }

        if (!string.IsNullOrWhiteSpace(context.PreviousWord) &&
            NextWord.TryGetValue(context.PreviousWord, out var nextWords))
        {
            return RankPersonal(nextWords);
        }

        return RankPersonal(["the", "I", "we"]);
    }

    public void LearnAcceptedSuggestion(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        _personalFrequency.TryGetValue(suggestion, out var frequency);
        _personalFrequency[suggestion] = frequency + 1;
    }

    private IEnumerable<string> CompleteWord(string prefix)
    {
        var candidates = Vocabulary
            .Concat(_personalFrequency.Keys)
            .Where(word => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                           !word.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return RankPersonal(candidates).Take(3);
    }

    private IEnumerable<string> RankPersonal(IEnumerable<string> words)
    {
        return words
            .OrderByDescending(word => _personalFrequency.TryGetValue(word, out var frequency) ? frequency : 0)
            .ThenBy(word => word.Length)
            .ThenBy(word => word, StringComparer.OrdinalIgnoreCase)
            .Take(3);
    }
}
