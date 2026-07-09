using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keebs;

internal sealed class TextPredictionEngine
{
    private const int CurrentProfileVersion = 6;
    private const int MaxLearnedWords = 5000;
    private const int MaxSuggestions = 4;
    private const int MaxNextWordsPerPrefix = 40;
    private const int MaxSpellCheckWordLength = 24;
    private const int MissingSeedRank = int.MaxValue;
    private const int UserNextWordWeight = 100;

    private static readonly Lazy<string[]> CommonVocabulary = new(() => LoadWordResource("Keebs.Assets.english-common-words.txt"));
    private static readonly Lazy<string[]> DictionaryVocabulary = new(() => LoadWordResource("Keebs.Assets.english-dictionary-words.txt"));
    private static readonly Lazy<HashSet<string>> DictionaryLookup = new(() => DictionaryVocabulary.Value.ToHashSet(StringComparer.OrdinalIgnoreCase));
    private static readonly Lazy<string[]> ExpandedCommonVocabulary = new(BuildExpandedCommonVocabulary);
    private static readonly Lazy<HashSet<string>> SeedVocabularyLookup = new(BuildSeedVocabularyLookup);
    private static readonly Lazy<Dictionary<string, int>> SeedRank = new(BuildSeedRank);
    private static readonly Lazy<PretrainedModel> Pretrained = new(BuildPretrainedModel);

    private readonly string _profilePath;
    private readonly Dictionary<string, int> _wordFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _acceptedFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _nextWordFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedSuggestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private static readonly Dictionary<string, string[]> NextWord = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = ["lot", "little", "few"],
        ["about"] = ["the", "that", "this"],
        ["after"] = ["another", "the", "that"],
        ["are"] = ["you", "we", "common"],
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
        ["normal"] = ["corrections", "field", "typing"],
        ["of"] = ["the", "this", "that"],
        ["on"] = ["the", "or", "windows"],
        ["or"] = ["is", "it", "the"],
        ["paths"] = ["should", "can", "will"],
        ["private"] = ["and", "data", "typing"],
        ["runs"] = ["local", "and", "the"],
        ["that"] = ["works", "would", "is"],
        ["the"] = ["keyboard", "app", "text"],
        ["this"] = ["is", "works", "should"],
        ["to"] = ["the", "be", "use"],
        ["traces"] = ["against", "and", "from"],
        ["testing"] = ["contractions", "the", "this"],
        ["we"] = ["can", "should", "could"],
        ["what"] = ["about", "is", "we"],
        ["windows"] = ["keyboard", "osk", "app"],
        ["with"] = ["the", "a", "this"],
        ["would"] = ["be", "work", "need"],
        ["you"] = ["can", "should", "want"]
    };

    private static readonly string[] BuiltInVocabulary =
    [
        "able", "about", "accessibility", "actually", "admin", "after", "again", "almost", "already",
        "also", "always", "another", "application", "around", "autocorrect", "available", "because",
        "before", "behavior", "better", "between", "browser", "button", "candidate", "change",
        "clipboard", "completion", "computer", "context", "control", "could", "desktop", "dictionary", "different",
        "doing", "elevated", "enough", "every", "example", "field", "floating", "focus", "going",
        "great", "hello", "important", "input", "issue", "keyboard", "layout", "learning", "local",
        "maybe", "model", "native", "never", "offline", "password", "please", "prediction", "pretty",
        "private", "probably", "problem", "program", "really", "replacement", "right", "secure",
        "sensitive", "service", "settings", "shortcut", "should", "something", "standard", "still",
        "suggestion", "surface", "system", "thanks", "their", "there", "these", "thing", "think",
        "through", "typing", "until", "using", "wanted", "where", "window", "windows", "without",
        "working", "would"
    ];

    private static readonly string[] BuiltInContractions =
    [
        "aren't", "can't", "couldn't", "didn't", "doesn't", "don't", "hadn't", "hasn't", "haven't",
        "he'd", "he'll", "he's", "here's", "how's", "i'd", "i'll", "i'm", "i've", "isn't",
        "it'd", "it'll", "it's", "let's", "mightn't", "mustn't", "she'd", "she'll", "she's",
        "shouldn't", "that's", "there's", "they'd", "they'll", "they're", "they've", "wasn't",
        "we'd", "we'll", "we're", "we've", "weren't", "what's", "where's", "who's", "won't",
        "wouldn't", "you'd", "you'll", "you're", "you've"
    ];

    private static readonly string[] BuiltInSwipeVocabulary =
    [
        "the", "and", "you", "that", "this", "have", "for", "not", "with", "are", "but", "can", "is", "it", "ok", "on", "now",
        "all", "was", "or", "we", "what", "when", "where", "why", "how", "hello", "thanks", "please", "quick",
        "keyboard", "prediction", "tomorrow", "installation", "typing", "window", "screen", "update", "updates",
        "install", "release", "touchy", "fragile", "forgive", "path", "paths", "mistakes", "compare", "scores", "should",
        "fires", "ish", "first", "guess", "stays", "visible", "short", "words", "drift", "little", "messy", "fingers",
        "normal", "corrections", "interrupt", "traces", "against", "saved", "runs", "training", "data", "stay",
        "useful", "testing", "contractions", "common", "will", "tune", "after", "another", "noisy", "sample"
    ];

    private static readonly string[] CommonBackfillVocabulary =
    [
        "app", "apps", "browser", "browsers", "callback", "callbacks", "checkbox", "checkboxes", "commit", "commits",
        "corpus", "debug", "debugging", "discord", "feedback", "firefox", "github", "install", "installer", "ish",
        "messy", "profile", "profiles", "prompt", "prompts", "release", "sample", "samples", "seed", "seeding",
        "swipe", "swipes", "textbox", "textboxes", "toggle", "toggles", "trace", "traces", "typo", "typos"
    ];

    public TextPredictionEngine()
        : this(GetDefaultProfilePath())
    {
    }

    internal TextPredictionEngine(string profilePath)
    {
        _profilePath = profilePath;
        LoadProfile();
    }

    public IEnumerable<string> GetSuggestions(PredictionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.CurrentWord))
        {
            return CompleteWord(context.CurrentWord, context.PreviousWord);
        }

        if (!string.IsNullOrWhiteSpace(context.PreviousWord))
        {
            var nextWords = Enumerable.Empty<string>();

            if (NextWord.TryGetValue(context.PreviousWord, out var builtInNextWords))
            {
                nextWords = nextWords.Concat(builtInNextWords);
            }

            if (_nextWordFrequency.TryGetValue(context.PreviousWord, out var learnedNextWords))
            {
                nextWords = nextWords.Concat(learnedNextWords.Keys);
            }

            if (Pretrained.Value.NextWordFrequency.TryGetValue(context.PreviousWord, out var pretrainedNextWords))
            {
                nextWords = nextWords.Concat(pretrainedNextWords.Keys);
            }

            var rankedNextWords = RankCandidates(nextWords, context.PreviousWord).ToArray();
            if (rankedNextWords.Length > 0)
            {
                return rankedNextWords;
            }
        }

        return RankCandidates(["the", "I", "we", "you"], string.Empty);
    }

    public IEnumerable<string> GetSwipeSuggestions(string tracedLetters, PredictionContext context)
    {
        return GetSwipeCandidates(tracedLetters, context).Take(MaxSuggestions);
    }

    public IEnumerable<string> GetSwipeCandidates(string tracedLetters, PredictionContext context, int maxCandidates = 96)
    {
        var pattern = NormalizeSwipePattern(tracedLetters);
        if (pattern.Length < 2)
        {
            return [];
        }

        var normalizedPreviousWord = NormalizeWord(context.PreviousWord);
        return GetSwipeVocabularyCandidates(pattern)
            .Select(NormalizeWord)
            .Where(word => word.Length >= 2)
            .Where(word => !IsRemovedSuggestion(word))
            .Where(word => !word.Contains('\''))
            .Where(word => word[0] == pattern[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(word => new
            {
                Word = word,
                Match = GetSwipeMatch(pattern, NormalizeSwipePattern(word))
            })
            .Where(candidate => candidate.Match is not null)
            .OrderBy(candidate => GetSwipeEditCost(candidate.Word, candidate.Match!))
            .ThenBy(candidate => candidate.Match!.IsExactMatch ? 0 : 1)
            .ThenBy(candidate => GetSeedRank(candidate.Word))
            .ThenBy(candidate => candidate.Match!.TraceIsSubsequenceOfWord ? 0 : 1)
            .ThenBy(candidate => candidate.Match!.SkippedLetters)
            .ThenByDescending(candidate => GetNextWordScore(normalizedPreviousWord, candidate.Word))
            .ThenByDescending(candidate => _acceptedFrequency.TryGetValue(candidate.Word, out var accepted) ? accepted : 0)
            .ThenByDescending(candidate => _wordFrequency.TryGetValue(candidate.Word, out var frequency) ? frequency : 0)
            .ThenByDescending(candidate => Pretrained.Value.WordFrequency.TryGetValue(candidate.Word, out var pretrainedFrequency) ? pretrainedFrequency : 0)
            .ThenBy(candidate => candidate.Word.Length)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates)
            .Select(candidate => FormatSuggestion(candidate.Word));
    }

    public void LearnTypedText(IEnumerable<TextCommit> commits)
    {
        var learned = false;

        foreach (var commit in commits)
        {
            learned |= LearnWord(commit.Word);
            learned |= LearnNextWord(commit.PreviousWord, commit.Word);
        }

        if (learned)
        {
            SaveProfile();
        }
    }

    public void LearnAcceptedSuggestion(string suggestion, string previousWord)
    {
        var word = NormalizeWord(suggestion);
        if (word.Length == 0 || IsRemovedSuggestion(word))
        {
            return;
        }

        Increment(_acceptedFrequency, word);
        LearnWord(word);
        LearnNextWord(previousWord, word);
        SaveProfile();
    }

    public void RemoveSuggestion(string suggestion)
    {
        var word = NormalizeWord(suggestion);
        if (word.Length == 0)
        {
            return;
        }

        _removedSuggestions.Add(word);
        _wordFrequency.Remove(word);
        _acceptedFrequency.Remove(word);
        _nextWordFrequency.Remove(word);

        foreach (var nextWords in _nextWordFrequency.Values)
        {
            nextWords.Remove(word);
        }

        SaveProfile();
    }

    internal int GetContextualNextWordScore(string previousWord, string word)
    {
        var normalizedPreviousWord = previousWord.Equals("w", StringComparison.OrdinalIgnoreCase)
            ? "we"
            : NormalizeWord(previousWord);
        var normalizedWord = NormalizeWord(word);
        if (normalizedPreviousWord.Length == 0 || normalizedWord.Length == 0)
        {
            return 0;
        }

        var score = GetNextWordScore(normalizedPreviousWord, normalizedWord);
        if (NextWord.TryGetValue(normalizedPreviousWord, out var builtInNextWords) &&
            builtInNextWords.Contains(normalizedWord, StringComparer.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }

    private IEnumerable<string> CompleteWord(string prefix, string previousWord)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var candidates = GetVocabularyCandidates()
            .Where(word => word.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) &&
                           !word.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var ranked = RankCandidates(candidates, string.Empty).ToList();
        if (ShouldOfferSpellingCorrections(normalizedPrefix, ranked))
        {
            var corrections = GetSpellingCorrections(normalizedPrefix, previousWord)
                .Where(correction => !ranked.Contains(correction, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (corrections.Count > 0)
            {
                corrections.AddRange(ranked);
                TrimSuggestions(corrections);
                return corrections;
            }
        }

        AddDictionaryFallbacks(ranked, normalizedPrefix);

        return ranked.Count == 0 && IsWordLikePrefix(prefix)
            ? IsRemovedSuggestion(normalizedPrefix) ? [] : [normalizedPrefix]
            : ranked;
    }

    private IEnumerable<string> GetVocabularyCandidates()
    {
        return BuiltInVocabulary
            .Concat(BuiltInContractions)
            .Concat(ExpandedCommonVocabulary.Value)
            .Concat(Pretrained.Value.WordFrequency.Keys)
            .Concat(_wordFrequency.Keys)
            .Concat(_acceptedFrequency.Keys);
    }

    private IEnumerable<string> GetSwipeVocabularyCandidates(string pattern)
    {
        var candidates = BuiltInSwipeVocabulary
            .Concat(BuiltInVocabulary)
            .Concat(BuiltInContractions)
            .Concat(ExpandedCommonVocabulary.Value)
            .Concat(Pretrained.Value.WordFrequency.Keys)
            .Concat(_acceptedFrequency.Keys)
            .Concat(GetDictionarySwipeCandidates(pattern));

        return pattern.Length < 5
            ? candidates
            : candidates.Concat(_wordFrequency.Keys);
    }

    private static IEnumerable<string> GetDictionarySwipeCandidates(string pattern)
    {
        if (pattern.Length < 5 || pattern.Length > 24)
        {
            yield break;
        }

        foreach (var word in GetDictionaryPrefixMatches(pattern[0].ToString()))
        {
            var wordPattern = NormalizeSwipePattern(word);
            if (Math.Abs(wordPattern.Length - pattern.Length) > GetMaximumSwipeLengthDifference(pattern.Length, wordPattern.Length))
            {
                continue;
            }

            if (GetSwipeMatch(pattern, wordPattern) is not null)
            {
                yield return word;
            }
        }
    }

    private static SwipeMatch? GetSwipeMatch(string pattern, string wordPattern)
    {
        if (wordPattern.Length < 2 || pattern[0] != wordPattern[0])
        {
            return null;
        }

        var maximumDistance = GetMaximumSwipeEditDistance(pattern.Length, wordPattern.Length);
        var editDistance = GetDamerauLevenshteinDistance(pattern, wordPattern, maximumDistance);
        var wordIsSubsequenceOfTrace = IsOrderedSubsequence(wordPattern, pattern);
        var traceIsSubsequenceOfWord = IsOrderedSubsequence(pattern, wordPattern);
        var lengthDifference = Math.Abs(pattern.Length - wordPattern.Length);
        if ((lengthDifference <= GetMaximumSwipeLengthDifference(pattern.Length, wordPattern.Length) ||
             wordIsSubsequenceOfTrace ||
             traceIsSubsequenceOfWord) &&
            (editDistance <= maximumDistance || wordIsSubsequenceOfTrace || traceIsSubsequenceOfWord))
        {
            return new SwipeMatch(editDistance, lengthDifference, traceIsSubsequenceOfWord, wordIsSubsequenceOfTrace, pattern == wordPattern);
        }

        return GetLooseNoisySwipeMatch(pattern, wordPattern, maximumDistance, lengthDifference);
    }

    private static SwipeMatch? GetLooseNoisySwipeMatch(
        string pattern,
        string wordPattern,
        int maximumDistance,
        int lengthDifference)
    {
        if (pattern.Length < 7 ||
            pattern.Length < wordPattern.Length + 3 ||
            wordPattern.Length < 3)
        {
            return null;
        }

        var orderedMatches = GetOrderedMatchCount(wordPattern, pattern);
        var coveredLetters = GetLetterCoverageCount(wordPattern, pattern);
        var requiredLetters = wordPattern.Length <= 3
            ? 2
            : Math.Max(4, (int)Math.Ceiling(wordPattern.Length * 0.62));
        var requiredCoverage = requiredLetters;

        if (orderedMatches < requiredLetters && coveredLetters < requiredCoverage)
        {
            return null;
        }

        if (!pattern.Contains(wordPattern[^1]) && Math.Max(orderedMatches, coveredLetters) < wordPattern.Length - 1)
        {
            return null;
        }

        var looseDistance = Math.Max(1, wordPattern.Length - Math.Max(orderedMatches, coveredLetters));
        return new SwipeMatch(looseDistance, lengthDifference, false, orderedMatches == wordPattern.Length, false);
    }

    private int GetSwipeEditCost(string word, SwipeMatch match)
    {
        var cost = match.EditDistance;

        if (match.TraceIsSubsequenceOfWord)
        {
            cost -= match.SkippedLetters >= 2 ? 3 : 2;
        }

        if (match.WordIsSubsequenceOfTrace)
        {
            cost = Math.Min(cost, 1);
            if (word.Length <= 2 && match.SkippedLetters >= 5)
            {
                cost += 4;
            }
        }

        if (IsHighConfidenceSwipeWord(word))
        {
            cost--;
        }
        else
        {
            cost++;
        }

        return Math.Max(0, cost);
    }

    private bool IsHighConfidenceSwipeWord(string word)
    {
        return SeedVocabularyLookup.Value.Contains(word) ||
               Pretrained.Value.WordFrequency.ContainsKey(word) ||
               _wordFrequency.ContainsKey(word) ||
               _acceptedFrequency.ContainsKey(word);
    }

    private static int GetMaximumSwipeEditDistance(int traceLength, int wordLength)
    {
        var length = Math.Max(traceLength, wordLength);
        return Math.Max(1, Math.Min(5, (int)Math.Ceiling(length * 0.45)));
    }

    private static int GetMaximumSwipeLengthDifference(int traceLength, int wordLength)
    {
        var length = Math.Max(traceLength, wordLength);
        return Math.Max(1, Math.Min(5, (int)Math.Ceiling(length * 0.4)));
    }

    private static bool IsOrderedSubsequence(string expectedLetters, string tracedLetters)
    {
        var expectedIndex = 0;

        foreach (var letter in tracedLetters)
        {
            if (letter != expectedLetters[expectedIndex])
            {
                continue;
            }

            expectedIndex++;
            if (expectedIndex == expectedLetters.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetOrderedMatchCount(string expectedLetters, string tracedLetters)
    {
        var expectedIndex = 0;

        foreach (var letter in tracedLetters)
        {
            if (expectedIndex >= expectedLetters.Length)
            {
                break;
            }

            if (letter == expectedLetters[expectedIndex])
            {
                expectedIndex++;
            }
        }

        return expectedIndex;
    }

    private static int GetLetterCoverageCount(string expectedLetters, string tracedLetters)
    {
        Span<int> counts = stackalloc int[26];

        foreach (var letter in tracedLetters)
        {
            if (letter is >= 'a' and <= 'z')
            {
                counts[letter - 'a']++;
            }
        }

        var covered = 0;
        foreach (var letter in expectedLetters)
        {
            if (letter is < 'a' or > 'z')
            {
                continue;
            }

            var index = letter - 'a';
            if (counts[index] <= 0)
            {
                continue;
            }

            counts[index]--;
            covered++;
        }

        return covered;
    }

    private static int GetLevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static bool IsWordLikePrefix(string prefix)
    {
        return prefix.Any(char.IsLetter);
    }

    private static int GetDamerauLevenshteinDistance(string left, string right, int maximumDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maximumDistance)
        {
            return maximumDistance + 1;
        }

        var previousPrevious = new int[right.Length + 1];
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];

            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                var distance = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);

                if (row > 1 &&
                    column > 1 &&
                    left[row - 1] == right[column - 2] &&
                    left[row - 2] == right[column - 1])
                {
                    distance = Math.Min(distance, previousPrevious[column - 2] + 1);
                }

                current[column] = distance;
                rowMinimum = Math.Min(rowMinimum, distance);
            }

            if (rowMinimum > maximumDistance)
            {
                return maximumDistance + 1;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[right.Length];
    }

    private IEnumerable<string> RankCandidates(IEnumerable<string> words, string previousWord)
    {
        var normalizedPreviousWord = NormalizeWord(previousWord);

        return words
            .Select(NormalizeWord)
            .Where(word => word.Length > 0)
            .Where(word => !IsRemovedSuggestion(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(word => GetNextWordScore(normalizedPreviousWord, word))
            .ThenByDescending(word => _acceptedFrequency.TryGetValue(word, out var accepted) ? accepted : 0)
            .ThenByDescending(word => _wordFrequency.TryGetValue(word, out var frequency) ? frequency : 0)
            .ThenByDescending(word => Pretrained.Value.WordFrequency.TryGetValue(word, out var frequency) ? frequency : 0)
            .ThenBy(word => GetSeedRank(word))
            .ThenBy(word => word.Length)
            .ThenBy(word => word, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .Select(FormatSuggestion);
    }

    private bool ShouldOfferSpellingCorrections(string word, IReadOnlyCollection<string> completions)
    {
        return word.Length is >= 3 and <= MaxSpellCheckWordLength &&
               word.Any(char.IsLetter) &&
               !IsKnownWord(word) &&
               (completions.Count == 0 || completions.All(IsLowConfidenceCompletion));
    }

    private IEnumerable<string> GetSpellingCorrections(string word, string previousWord)
    {
        var maximumDistance = GetMaximumSpellDistance(word);
        var normalizedPreviousWord = NormalizeWord(previousWord);

        return GetSpellCheckCandidates()
            .Select(NormalizeWord)
            .Where(candidate => IsSpellCheckCandidate(word, candidate, maximumDistance))
            .Where(candidate => !IsRemovedSuggestion(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new
            {
                Word = candidate,
                Distance = GetDamerauLevenshteinDistance(word, candidate, maximumDistance)
            })
            .Where(candidate => candidate.Distance <= maximumDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenByDescending(candidate => GetNextWordScore(normalizedPreviousWord, candidate.Word))
            .ThenByDescending(candidate => _acceptedFrequency.TryGetValue(candidate.Word, out var accepted) ? accepted : 0)
            .ThenByDescending(candidate => _wordFrequency.TryGetValue(candidate.Word, out var frequency) ? frequency : 0)
            .ThenByDescending(candidate => Pretrained.Value.WordFrequency.TryGetValue(candidate.Word, out var pretrainedFrequency) ? pretrainedFrequency : 0)
            .ThenBy(candidate => GetSeedRank(candidate.Word))
            .ThenBy(candidate => candidate.Word.Length)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .Select(candidate => FormatSuggestion(candidate.Word));
    }

    private IEnumerable<string> GetSpellCheckCandidates()
    {
        return BuiltInVocabulary
            .Concat(BuiltInContractions)
            .Concat(ExpandedCommonVocabulary.Value)
            .Concat(DictionaryVocabulary.Value)
            .Concat(Pretrained.Value.WordFrequency.Keys)
            .Concat(_wordFrequency.Keys)
            .Concat(_acceptedFrequency.Keys);
    }

    private static int GetMaximumSpellDistance(string word)
    {
        return word.Length <= 4 ? 1 : 2;
    }

    private static bool IsSpellCheckCandidate(string word, string candidate, int maximumDistance)
    {
        return candidate.Length > 0 &&
               candidate.Length <= MaxSpellCheckWordLength &&
               !candidate.Equals(word, StringComparison.OrdinalIgnoreCase) &&
               Math.Abs(candidate.Length - word.Length) <= maximumDistance &&
               SharesSpellCheckAnchor(word, candidate);
    }

    private static bool SharesSpellCheckAnchor(string word, string candidate)
    {
        return word[0] == candidate[0] ||
               word.Length > 1 && candidate.Length > 1 && word[0] == candidate[1] && word[1] == candidate[0];
    }

    private bool IsKnownWord(string word)
    {
        return DictionaryLookup.Value.Contains(word) ||
               SeedVocabularyLookup.Value.Contains(word) ||
               Pretrained.Value.WordFrequency.ContainsKey(word) ||
               _wordFrequency.ContainsKey(word) ||
               _acceptedFrequency.ContainsKey(word);
    }

    private bool IsLowConfidenceCompletion(string suggestion)
    {
        var word = NormalizeWord(suggestion);
        return word.Length == 0 ||
               !SeedVocabularyLookup.Value.Contains(word) &&
               !Pretrained.Value.WordFrequency.ContainsKey(word) &&
               !_wordFrequency.ContainsKey(word) &&
               !_acceptedFrequency.ContainsKey(word);
    }

    private int GetNextWordScore(string previousWord, string word)
    {
        if (previousWord.Length == 0)
        {
            return 0;
        }

        var score = 0;
        if (_nextWordFrequency.TryGetValue(previousWord, out var nextWords) &&
            nextWords.TryGetValue(word, out var frequency))
        {
            score += frequency * UserNextWordWeight;
        }

        if (Pretrained.Value.NextWordFrequency.TryGetValue(previousWord, out var pretrainedNextWords) &&
            pretrainedNextWords.TryGetValue(word, out var pretrainedFrequency))
        {
            score += pretrainedFrequency;
        }

        return score;
    }

    private bool LearnWord(string word)
    {
        var normalizedWord = NormalizeWord(word);
        if (normalizedWord.Length == 0 || IsRemovedSuggestion(normalizedWord))
        {
            return false;
        }

        Increment(_wordFrequency, normalizedWord);
        TrimDictionary(_wordFrequency, MaxLearnedWords);
        return true;
    }

    private bool LearnNextWord(string previousWord, string word)
    {
        var normalizedPreviousWord = NormalizeWord(previousWord);
        var normalizedWord = NormalizeWord(word);

        if (normalizedPreviousWord.Length == 0 ||
            normalizedWord.Length == 0 ||
            IsRemovedSuggestion(normalizedPreviousWord) ||
            IsRemovedSuggestion(normalizedWord))
        {
            return false;
        }

        if (!_nextWordFrequency.TryGetValue(normalizedPreviousWord, out var nextWords))
        {
            nextWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _nextWordFrequency[normalizedPreviousWord] = nextWords;
        }

        Increment(nextWords, normalizedWord);
        TrimDictionary(nextWords, MaxNextWordsPerPrefix);
        return true;
    }

    private void LoadProfile()
    {
        if (!File.Exists(_profilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_profilePath);
            var profile = JsonSerializer.Deserialize<PredictionProfile>(json, _jsonOptions);
            if (profile is null)
            {
                return;
            }

            var shouldSaveMigratedProfile = profile.Version < CurrentProfileVersion;

            CopyInto(profile.WordFrequency, _wordFrequency);
            CopyInto(profile.AcceptedFrequency, _acceptedFrequency);
            CopyRemovedSuggestions(profile.RemovedSuggestions);

            foreach (var (previousWord, nextWords) in profile.NextWordFrequency)
            {
                var normalizedPreviousWord = NormalizeWord(previousWord);
                if (normalizedPreviousWord.Length == 0 || IsRemovedSuggestion(normalizedPreviousWord))
                {
                    continue;
                }

                var target = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                CopyInto(nextWords, target);
                foreach (var removedSuggestion in _removedSuggestions)
                {
                    target.Remove(removedSuggestion);
                }

                if (target.Count > 0)
                {
                    _nextWordFrequency[normalizedPreviousWord] = target;
                }
            }

            if (shouldSaveMigratedProfile)
            {
                SaveProfile();
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void SaveProfile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_profilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var profile = new PredictionProfile
            {
                Version = CurrentProfileVersion,
                WordFrequency = OrderedCopy(_wordFrequency),
                AcceptedFrequency = OrderedCopy(_acceptedFrequency),
                RemovedSuggestions = _removedSuggestions
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                NextWordFrequency = _nextWordFrequency
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => OrderedCopy(pair.Value),
                        StringComparer.OrdinalIgnoreCase)
            };

            var json = JsonSerializer.Serialize(profile, _jsonOptions);
            var temporaryPath = $"{_profilePath}.tmp";
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(_profilePath))
            {
                File.Replace(temporaryPath, _profilePath, null);
            }
            else
            {
                File.Move(temporaryPath, _profilePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Dictionary<string, int> OrderedCopy(Dictionary<string, int> source)
    {
        return source
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void CopyInto(IReadOnlyDictionary<string, int> source, Dictionary<string, int> target)
    {
        foreach (var (word, frequency) in source)
        {
            var normalizedWord = NormalizeWord(word);
            if (normalizedWord.Length == 0 || frequency <= 0)
            {
                continue;
            }

            target[normalizedWord] = frequency;
        }
    }

    private void CopyRemovedSuggestions(IEnumerable<string> source)
    {
        foreach (var suggestion in source)
        {
            var word = NormalizeWord(suggestion);
            if (word.Length > 0)
            {
                _removedSuggestions.Add(word);
                _wordFrequency.Remove(word);
                _acceptedFrequency.Remove(word);
            }
        }
    }

    private static void Increment(Dictionary<string, int> frequency, string word)
    {
        frequency.TryGetValue(word, out var count);
        frequency[word] = count == int.MaxValue ? int.MaxValue : count + 1;
    }

    private static void TrimDictionary(Dictionary<string, int> frequency, int maxEntries)
    {
        if (frequency.Count <= maxEntries)
        {
            return;
        }

        foreach (var word in frequency
                     .OrderBy(pair => pair.Value)
                     .ThenByDescending(pair => pair.Key.Length)
                     .Take(frequency.Count - maxEntries)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            frequency.Remove(word);
        }
    }

    private static string NormalizePrefix(string value)
    {
        return new string(value
            .Trim()
            .Select(character => character == '’' ? '\'' : char.ToLowerInvariant(character))
            .Where(character => char.IsLetter(character) || character == '\'')
            .ToArray());
    }

    private static string NormalizeWord(string value)
    {
        var normalized = NormalizePrefix(value).Trim('\'');
        if (IsRejectedPredictionWord(normalized))
        {
            return string.Empty;
        }

        return normalized.Length > 1 || normalized.Equals("i", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("a", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : string.Empty;
    }

    private static string NormalizeSwipePattern(string value)
    {
        var letters = new List<char>();
        char? previous = null;

        foreach (var character in value)
        {
            var normalized = char.ToLowerInvariant(character);
            if (!char.IsLetter(normalized) || normalized == previous)
            {
                continue;
            }

            letters.Add(normalized);
            previous = normalized;
        }

        return new string([.. letters]);
    }

    private static bool IsRejectedPredictionWord(string word)
    {
        return word.StartsWith("xhtml", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("iuds", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("ires", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("nk", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("oik", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("ui", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsRemovedSuggestion(string word)
    {
        return _removedSuggestions.Contains(word);
    }

    private static string FormatSuggestion(string word)
    {
        if (word.Equals("i", StringComparison.OrdinalIgnoreCase))
        {
            return "I";
        }

        return word.StartsWith("i'", StringComparison.OrdinalIgnoreCase)
            ? $"I{word[1..]}"
            : word;
    }

    private static string[] BuildExpandedCommonVocabulary()
    {
        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string word)
        {
            var normalizedWord = NormalizeWord(word);
            if (normalizedWord.Length > 0 && seen.Add(normalizedWord))
            {
                words.Add(normalizedWord);
            }
        }

        foreach (var word in CommonVocabulary.Value)
        {
            Add(word);
            AddCommonInflections(word, Add);
        }

        foreach (var word in CommonBackfillVocabulary)
        {
            Add(word);
            AddCommonInflections(word, Add);
        }

        return [.. words];
    }

    private static void AddCommonInflections(string word, Action<string> add)
    {
        if (word.Length < 3 || word.Length > 18)
        {
            return;
        }

        AddIfDictionaryWord($"{word}s", add);
        AddIfDictionaryWord($"{word}es", add);
        AddIfDictionaryWord($"{word}ed", add);
        AddIfDictionaryWord($"{word}ing", add);
        AddIfDictionaryWord($"{word}er", add);
        AddIfDictionaryWord($"{word}est", add);
        AddIfDictionaryWord($"{word}ly", add);

        if (word.EndsWith('e'))
        {
            AddIfDictionaryWord($"{word[..^1]}ed", add);
            AddIfDictionaryWord($"{word[..^1]}ing", add);
        }

        if (word.EndsWith('y') && word.Length > 3)
        {
            AddIfDictionaryWord($"{word[..^1]}ies", add);
            AddIfDictionaryWord($"{word[..^1]}ied", add);
            AddIfDictionaryWord($"{word[..^1]}ier", add);
            AddIfDictionaryWord($"{word[..^1]}iest", add);
        }
    }

    private static void AddIfDictionaryWord(string word, Action<string> add)
    {
        if (DictionaryLookup.Value.Contains(word))
        {
            add(word);
        }
    }

    private static int GetSeedRank(string word)
    {
        return SeedRank.Value.TryGetValue(word, out var rank) ? rank : MissingSeedRank;
    }

    private static Dictionary<string, int> BuildSeedRank()
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var word in BuiltInSwipeVocabulary.Concat(BuiltInVocabulary).Concat(BuiltInContractions).Concat(ExpandedCommonVocabulary.Value))
        {
            var normalizedWord = NormalizeWord(word);
            if (normalizedWord.Length == 0 || rank.ContainsKey(normalizedWord))
            {
                continue;
            }

            rank[normalizedWord] = index++;
        }

        return rank;
    }

    private static HashSet<string> BuildSeedVocabularyLookup()
    {
        return BuiltInVocabulary
            .Concat(BuiltInSwipeVocabulary)
            .Concat(BuiltInContractions)
            .Concat(ExpandedCommonVocabulary.Value)
            .Select(NormalizeWord)
            .Where(word => word.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static PretrainedModel BuildPretrainedModel()
    {
        var wordFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nextWordFrequency = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var contraction in BuiltInContractions)
        {
            Increment(wordFrequency, NormalizeWord(contraction));
        }

        var assembly = typeof(TextPredictionEngine).Assembly;
        using var stream = assembly.GetManifestResourceStream("Keebs.Assets.pretrain-corpus.txt");
        if (stream is null)
        {
            return new PretrainedModel(wordFrequency, nextWordFrequency);
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            AddTrainingLine(line, wordFrequency, nextWordFrequency);
        }

        return new PretrainedModel(wordFrequency, nextWordFrequency);
    }

    private static void AddTrainingLine(
        string line,
        Dictionary<string, int> wordFrequency,
        Dictionary<string, Dictionary<string, int>> nextWordFrequency)
    {
        var previousWord = string.Empty;
        foreach (var word in ExtractWords(line))
        {
            Increment(wordFrequency, word);

            if (previousWord.Length > 0)
            {
                if (!nextWordFrequency.TryGetValue(previousWord, out var nextWords))
                {
                    nextWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    nextWordFrequency[previousWord] = nextWords;
                }

                Increment(nextWords, word);
            }

            previousWord = word;
        }
    }

    private static IEnumerable<string> ExtractWords(string text)
    {
        var word = new List<char>();

        foreach (var character in text)
        {
            var normalizedCharacter = character == '’' ? '\'' : char.ToLowerInvariant(character);
            if (char.IsLetter(normalizedCharacter) || normalizedCharacter == '\'')
            {
                word.Add(normalizedCharacter);
                continue;
            }

            if (word.Count > 0)
            {
                var normalizedWord = NormalizeWord(new string([.. word]));
                if (normalizedWord.Length > 0)
                {
                    yield return normalizedWord;
                }

                word.Clear();
            }
        }

        if (word.Count > 0)
        {
            var normalizedWord = NormalizeWord(new string([.. word]));
            if (normalizedWord.Length > 0)
            {
                yield return normalizedWord;
            }
        }
    }

    private void AddDictionaryFallbacks(List<string> suggestions, string prefix)
    {
        if (prefix.Length == 0)
        {
            return;
        }

        if (DictionaryLookup.Value.Contains(prefix) &&
            !IsRemovedSuggestion(prefix) &&
            !suggestions.Contains(prefix, StringComparer.OrdinalIgnoreCase))
        {
            var insertionIndex = GetExactMatchInsertionIndex(suggestions);
            suggestions.Insert(insertionIndex, FormatSuggestion(prefix));
            TrimSuggestions(suggestions);
        }

        if (suggestions.Count >= MaxSuggestions)
        {
            return;
        }

        foreach (var word in GetDictionaryPrefixMatches(prefix)
                     .Where(word => !word.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                                    !IsRemovedSuggestion(word) &&
                                    !suggestions.Contains(word, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(GetSeedRank)
                     .ThenBy(word => word.Length)
                     .ThenBy(word => word, StringComparer.OrdinalIgnoreCase))
        {
            suggestions.Add(FormatSuggestion(word));
            if (suggestions.Count >= MaxSuggestions)
            {
                return;
            }
        }
    }

    private static IEnumerable<string> GetDictionaryPrefixMatches(string prefix)
    {
        var words = DictionaryVocabulary.Value;
        var index = Array.BinarySearch(words, prefix, StringComparer.OrdinalIgnoreCase);

        if (index < 0)
        {
            index = ~index;
        }
        else
        {
            while (index > 0 && words[index - 1].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                index--;
            }
        }

        for (var i = index; i < words.Length && words[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase); i++)
        {
            yield return words[i];
        }
    }

    private static void TrimSuggestions(List<string> suggestions)
    {
        while (suggestions.Count > MaxSuggestions)
        {
            suggestions.RemoveAt(suggestions.Count - 1);
        }
    }

    private int GetExactMatchInsertionIndex(List<string> suggestions)
    {
        var lastPersonalIndex = suggestions.FindLastIndex(suggestion =>
        {
            var word = NormalizeWord(suggestion);
            return _wordFrequency.ContainsKey(word) || _acceptedFrequency.ContainsKey(word);
        });

        return lastPersonalIndex >= 0 ? lastPersonalIndex + 1 : 0;
    }

    private static string[] LoadWordResource(string resourceName)
    {
        var assembly = typeof(TextPredictionEngine).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        var words = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            var word = NormalizeWord(line);
            if (word.Length > 0)
            {
                words.Add(word);
            }
        }

        return words.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetDefaultProfilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Keebs",
            "prediction-profile.json");
    }

    private sealed class PredictionProfile
    {
        public int Version { get; init; } = 1;

        public Dictionary<string, int> WordFrequency { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> AcceptedFrequency { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public string[] RemovedSuggestions { get; init; } = [];

        public Dictionary<string, Dictionary<string, int>> NextWordFrequency { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PretrainedModel(
        Dictionary<string, int> WordFrequency,
        Dictionary<string, Dictionary<string, int>> NextWordFrequency);

    private sealed record SwipeMatch(
        int EditDistance,
        int SkippedLetters,
        bool TraceIsSubsequenceOfWord,
        bool WordIsSubsequenceOfTrace,
        bool IsExactMatch);
}
