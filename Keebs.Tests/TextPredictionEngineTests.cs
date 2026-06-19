namespace Keebs.Tests;

public sealed class TextPredictionEngineTests
{
    [Fact]
    public void CompletesKnownCurrentWordPrefix()
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext("key", [])).ToArray();

        Assert.Contains("keyboard", suggestions);
    }

    [Fact]
    public void KeepsTypedWordWhenPrefixHasNoCompletion()
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext("qzx", [])).ToArray();

        Assert.Equal(["qzx"], suggestions);
    }

    [Fact]
    public void ReturnsStarterSuggestionsWhenThereIsNoContext()
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal(4, suggestions.Length);
        Assert.Contains("I", suggestions);
        Assert.Contains("we", suggestions);
        Assert.Contains("the", suggestions);
        Assert.Contains("you", suggestions);
    }

    [Fact]
    public void CompletesWordsFromSeededEnglishDictionary()
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext("speci", [])).ToArray();

        Assert.Contains("species", suggestions);
    }

    [Theory]
    [InlineData("cat", "cat")]
    [InlineData("dog", "dog")]
    [InlineData("compu", "computer")]
    public void CompletesWordsFromBroadEnglishDictionary(string prefix, string expectedSuggestion)
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext(prefix, [])).ToArray();

        Assert.Contains(expectedSuggestion, suggestions);
    }

    [Fact]
    public void CompletesCommonContractions()
    {
        var engine = CreateEngine();

        var dontSuggestions = engine.GetSuggestions(new PredictionContext("don", [])).ToArray();
        var iSuggestions = engine.GetSuggestions(new PredictionContext("i'", [])).ToArray();

        Assert.Contains("don't", dontSuggestions);
        Assert.Contains("I'm", iSuggestions);
    }

    [Theory]
    [InlineData("teh", "the")]
    [InlineData("recieve", "receive")]
    [InlineData("adress", "address")]
    public void SuggestsSpellingCorrectionsWhenCurrentWordIsMisspelled(string misspelledWord, string correction)
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext(misspelledWord, [])).ToArray();

        Assert.Equal(correction, suggestions[0]);
    }

    [Fact]
    public void DoesNotSpellCorrectKnownTypedWords()
    {
        var engine = CreateEngine();
        engine.LearnTypedText([new TextCommit("quincboard", string.Empty)]);

        var suggestions = engine.GetSuggestions(new PredictionContext("quincboard", [])).ToArray();

        Assert.Equal("quincboard", suggestions[0]);
    }

    [Fact]
    public void UsesBundledCorpusForNextWordSuggestions()
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext(string.Empty, ["don't"])).ToArray();

        Assert.Equal("know", suggestions[0]);
    }

    [Fact]
    public void PersistsLearnedWordsAcrossEngineInstances()
    {
        var profilePath = GetProfilePath();
        var engine = new TextPredictionEngine(profilePath);

        engine.LearnTypedText([new TextCommit("quincboard", string.Empty)]);

        var reloadedEngine = new TextPredictionEngine(profilePath);
        var suggestions = reloadedEngine.GetSuggestions(new PredictionContext("quin", [])).ToArray();

        Assert.Equal("quincboard", suggestions[0]);
    }

    [Fact]
    public void MigratesExistingProfileWithoutLosingLearnedWords()
    {
        var profilePath = GetProfilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(
            profilePath,
            """
            {
              "Version": 1,
              "WordFrequency": {
                "quincboard": 7
              },
              "AcceptedFrequency": {},
              "NextWordFrequency": {}
            }
            """);

        var engine = new TextPredictionEngine(profilePath);
        var suggestions = engine.GetSuggestions(new PredictionContext("quin", [])).ToArray();
        var migratedJson = File.ReadAllText(profilePath);

        Assert.Equal("quincboard", suggestions[0]);
        Assert.Contains("\"Version\": 3", migratedJson);
    }

    [Fact]
    public void MigratesAwayKnownAccessibilityArtifacts()
    {
        var profilePath = GetProfilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(
            profilePath,
            """
            {
              "Version": 2,
              "WordFrequency": {
                "xhtml": 10,
                "queryword": 3
              },
              "AcceptedFrequency": {},
              "NextWordFrequency": {}
            }
            """);

        var engine = new TextPredictionEngine(profilePath);
        var artifactSuggestions = engine.GetSuggestions(new PredictionContext("xht", [])).ToArray();
        var realSuggestions = engine.GetSuggestions(new PredictionContext("quer", [])).ToArray();
        var migratedJson = File.ReadAllText(profilePath);

        Assert.DoesNotContain("xhtml", artifactSuggestions);
        Assert.Equal("queryword", realSuggestions[0]);
        Assert.DoesNotContain("xhtml", migratedJson);
        Assert.Contains("\"Version\": 3", migratedJson);
    }

    [Fact]
    public void LearnedNextWordPairsAreRankedAheadOfGenericSuggestions()
    {
        var engine = CreateEngine();

        engine.LearnTypedText(
        [
            new TextCommit("keyboard", string.Empty),
            new TextCommit("skin", "keyboard"),
            new TextCommit("skin", "keyboard")
        ]);

        var suggestions = engine.GetSuggestions(new PredictionContext(string.Empty, ["keyboard"])).ToArray();

        Assert.Equal("skin", suggestions[0]);
    }

    [Fact]
    public void ResolvesSwipeTraceWithRepeatedLetters()
    {
        var engine = CreateEngine();

        var suggestions = engine.GetSwipeSuggestions("hheelloo", new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal("hello", suggestions[0]);
    }

    [Fact]
    public void ResolvesSwipeTraceWithIncidentalKeys()
    {
        var engine = CreateEngine();

        var suggestions = engine.GetSwipeSuggestions("keuyboard", new PredictionContext(string.Empty, [])).ToArray();

        Assert.Contains("keyboard", suggestions);
    }

    [Fact]
    public void ResolvesLearnedSwipeWords()
    {
        var engine = CreateEngine();
        engine.LearnTypedText([new TextCommit("quincboard", string.Empty)]);

        var suggestions = engine.GetSwipeSuggestions("qincbord", new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal("quincboard", suggestions[0]);
    }

    private static TextPredictionEngine CreateEngine()
    {
        return new TextPredictionEngine(GetProfilePath());
    }

    private static string GetProfilePath()
    {
        return Path.Combine(Path.GetTempPath(), "Keebs.Tests", $"{Guid.NewGuid():N}", "prediction-profile.json");
    }
}
