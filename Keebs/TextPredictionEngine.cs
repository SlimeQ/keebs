using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keebs;

internal sealed class TextPredictionEngine
{
    private const int CurrentProfileVersion = 3;
    private const int MaxLearnedWords = 5000;
    private const int MaxSuggestions = 4;
    private const int MaxNextWordsPerPrefix = 40;
    private const int MissingSeedRank = int.MaxValue;
    private const int UserNextWordWeight = 100;

    private static readonly Lazy<string[]> CommonVocabulary = new(() => LoadWordResource("Keebs.Assets.english-common-words.txt"));
    private static readonly Lazy<string[]> DictionaryVocabulary = new(() => LoadWordResource("Keebs.Assets.english-dictionary-words.txt"));
    private static readonly Lazy<HashSet<string>> DictionaryLookup = new(() => DictionaryVocabulary.Value.ToHashSet(StringComparer.OrdinalIgnoreCase));
    private static readonly Lazy<Dictionary<string, int>> SeedRank = new(BuildSeedRank);
    private static readonly Lazy<PretrainedModel> Pretrained = new(BuildPretrainedModel);

    private readonly string _profilePath;
    private readonly Dictionary<string, int> _wordFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _acceptedFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _nextWordFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

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
            return CompleteWord(context.CurrentWord);
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
        if (word.Length == 0)
        {
            return;
        }

        Increment(_acceptedFrequency, word);
        LearnWord(word);
        LearnNextWord(previousWord, word);
        SaveProfile();
    }

    private IEnumerable<string> CompleteWord(string prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var candidates = BuiltInVocabulary
            .Concat(BuiltInContractions)
            .Concat(CommonVocabulary.Value)
            .Concat(Pretrained.Value.WordFrequency.Keys)
            .Concat(_wordFrequency.Keys)
            .Concat(_acceptedFrequency.Keys)
            .Where(word => word.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) &&
                           !word.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var ranked = RankCandidates(candidates, string.Empty).ToList();
        AddDictionaryFallbacks(ranked, normalizedPrefix);

        return ranked.Count == 0 && IsWordLikePrefix(prefix)
            ? [normalizedPrefix]
            : ranked;
    }

    private static bool IsWordLikePrefix(string prefix)
    {
        return prefix.Any(char.IsLetter);
    }

    private IEnumerable<string> RankCandidates(IEnumerable<string> words, string previousWord)
    {
        var normalizedPreviousWord = NormalizeWord(previousWord);

        return words
            .Select(NormalizeWord)
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(word => GetNextWordScore(normalizedPreviousWord, word))
            .ThenByDescending(word => _acceptedFrequency.TryGetValue(word, out var accepted) ? accepted : 0)
            .ThenByDescending(word => _wordFrequency.TryGetValue(word, out var frequency) ? frequency : 0)
            .ThenByDescending(word => Pretrained.Value.WordFrequency.TryGetValue(word, out var frequency) ? frequency : 0)
            .ThenBy(GetSeedRank)
            .ThenBy(word => word.Length)
            .ThenBy(word => word, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .Select(FormatSuggestion);
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
        if (normalizedWord.Length == 0)
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

        if (normalizedPreviousWord.Length == 0 || normalizedWord.Length == 0)
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

            foreach (var (previousWord, nextWords) in profile.NextWordFrequency)
            {
                var normalizedPreviousWord = NormalizeWord(previousWord);
                if (normalizedPreviousWord.Length == 0)
                {
                    continue;
                }

                var target = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                CopyInto(nextWords, target);
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

    private static bool IsRejectedPredictionWord(string word)
    {
        return word.Equals("xhtml", StringComparison.OrdinalIgnoreCase);
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

    private static int GetSeedRank(string word)
    {
        return SeedRank.Value.TryGetValue(word, out var rank) ? rank : MissingSeedRank;
    }

    private static Dictionary<string, int> BuildSeedRank()
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var word in BuiltInVocabulary.Concat(BuiltInContractions).Concat(CommonVocabulary.Value))
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

        public Dictionary<string, Dictionary<string, int>> NextWordFrequency { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PretrainedModel(
        Dictionary<string, int> WordFrequency,
        Dictionary<string, Dictionary<string, int>> NextWordFrequency);
}
