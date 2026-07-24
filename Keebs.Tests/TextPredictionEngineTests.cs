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

    [Theory]
    [InlineData("oyu", "you")]
    [InlineData("hte", "the")]
    [InlineData("tihs", "this")]
    public void SuggestsCorrectionsForTransposedLeadingLetters(string misspelledWord, string correction)
    {
        // Spell checking only walks the buckets for the first two letters, so a
        // correction that swaps them has to be reachable from either one.
        var engine = CreateEngine();
        var suggestions = engine.GetSuggestions(new PredictionContext(misspelledWord, [])).ToArray();

        Assert.Contains(correction, suggestions);
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
        Assert.Contains("\"Version\": 6", migratedJson);
    }

    [Fact]
    public void MigratedProfileCombinesLocalDictionaryWithExpandedLanguageBase()
    {
        var profilePath = GetProfilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(
            profilePath,
            """
            {
              "Version": 3,
              "WordFrequency": {
                "quincboard": 7
              },
              "AcceptedFrequency": {
                "quincboard": 3
              },
              "NextWordFrequency": {
                "quincboard": {
                  "skin": 5
                }
              }
            }
            """);

        var engine = new TextPredictionEngine(profilePath);
        var localSuggestions = engine.GetSuggestions(new PredictionContext("quin", [])).ToArray();
        var expandedSuggestions = engine.GetSuggestions(new PredictionContext("mess", [])).ToArray();
        var swipeSuggestions = engine.GetSwipeSuggestions("sample", new PredictionContext(string.Empty, [])).ToArray();
        var nextWordSuggestions = engine.GetSuggestions(new PredictionContext(string.Empty, ["quincboard"])).ToArray();
        var migratedJson = File.ReadAllText(profilePath);

        Assert.Equal("quincboard", localSuggestions[0]);
        Assert.Contains("messy", expandedSuggestions);
        Assert.Equal("sample", swipeSuggestions[0]);
        Assert.Equal("skin", nextWordSuggestions[0]);
        Assert.Contains("\"quincboard\": 7", migratedJson);
        Assert.Contains("\"Version\": 6", migratedJson);
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
                "xhtmlopenclaw": 9,
                "iuds": 8,
                "queryword": 3
              },
              "AcceptedFrequency": {
                "oik": 4
              },
              "NextWordFrequency": {}
            }
            """);

        var engine = new TextPredictionEngine(profilePath);
        var artifactSuggestions = engine.GetSuggestions(new PredictionContext("xht", [])).ToArray();
        var realSuggestions = engine.GetSuggestions(new PredictionContext("quer", [])).ToArray();
        var migratedJson = File.ReadAllText(profilePath);

        Assert.DoesNotContain("xhtml", artifactSuggestions);
        Assert.DoesNotContain("xhtmlopenclaw", migratedJson);
        Assert.Equal("queryword", realSuggestions[0]);
        Assert.DoesNotContain("xhtml", migratedJson);
        Assert.DoesNotContain("iuds", migratedJson);
        Assert.DoesNotContain("oik", migratedJson);
        Assert.Contains("\"Version\": 6", migratedJson);
    }

    [Theory]
    [InlineData("xhtml")]
    [InlineData("xhtmlque")]
    [InlineData("XHTMLsearc")]
    public void UnknownPrefixesCarryingBrowserMetadataAreNotEchoedBack(string prefix)
    {
        var engine = CreateEngine();

        var suggestions = engine.GetSuggestions(new PredictionContext(prefix, [])).ToArray();

        Assert.DoesNotContain(suggestions, suggestion => suggestion.StartsWith("xhtml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WordsLearnedInAFieldWithHiddenTextAreStillSuggestedThere()
    {
        var engine = CreateEngine();
        var session = new TextSession();

        // Typing the address a few times is what the URL bar teaches Keebs.
        engine.LearnTypedText(
        [
            new TextCommit("openclaw", string.Empty),
            new TextCommit("openclaw", string.Empty),
            new TextCommit("openclaw", string.Empty)
        ]);

        // The next read of that field prepends its hidden text to the input.
        session.SeedFromTextBeforeCaret(
            FocusedTextContextReader.SanitizeSeedText("xhtmlSearch with Google\u200Bopencl"));

        Assert.Equal("opencl", session.Context.CurrentWord);
        Assert.Equal("openclaw", engine.GetSuggestions(session.Context).First());
    }

    [Fact]
    public void UnknownTypedPrefixesAreStillEchoedBack()
    {
        var engine = CreateEngine();

        var suggestions = engine.GetSuggestions(new PredictionContext("zzqwertyz", [])).ToArray();

        Assert.Equal("zzqwertyz", suggestions.Single());
    }

    [Fact]
    public void RemovedSuggestionsAreSuppressedAndPersisted()
    {
        var profilePath = GetProfilePath();
        var engine = new TextPredictionEngine(profilePath);

        engine.LearnTypedText([new TextCommit("secretword", string.Empty)]);
        Assert.Equal("secretword", engine.GetSuggestions(new PredictionContext("secretw", [])).First());

        engine.RemoveSuggestion("secretword");

        var suggestions = engine.GetSuggestions(new PredictionContext("secretw", [])).ToArray();
        var reloaded = new TextPredictionEngine(profilePath);
        var reloadedSuggestions = reloaded.GetSuggestions(new PredictionContext("secretw", [])).ToArray();
        var json = File.ReadAllText(profilePath);

        Assert.DoesNotContain("secretword", suggestions);
        Assert.DoesNotContain("secretword", reloadedSuggestions);
        Assert.Contains("\"secretword\"", json);
        Assert.Contains("\"RemovedSuggestions\"", json);
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

    [Theory]
    [InlineData("teh", "the")]
    [InlineData("quik", "quick")]
    public void SwipeSuggestionsPreferCommonWordsForMinorTraceMistakes(string trace, string expectedSuggestion)
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSwipeSuggestions(trace, new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal(expectedSuggestion, suggestions[0]);
    }

    [Theory]
    [InlineData("iuds", "is")]
    [InlineData("oik", "ok")]
    public void SwipeSuggestionsIgnoreLearnedShortTypoArtifacts(string trace, string expectedSuggestion)
    {
        var engine = CreateEngine();
        engine.LearnTypedText([new TextCommit(trace, string.Empty)]);
        engine.LearnAcceptedSuggestion(trace, string.Empty);

        var suggestions = engine.GetSwipeSuggestions(trace, new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal(expectedSuggestion, suggestions[0]);
    }

    [Theory]
    [InlineData("finger", "fingers")]
    [InlineData("correction", "corrections")]
    [InlineData("contraction", "contractions")]
    [InlineData("fire", "fires")]
    public void SuggestionsIncludeDictionaryConfirmedCommonInflections(string prefix, string expectedSuggestion)
    {
        var engine = CreateEngine();

        var suggestions = engine.GetSuggestions(new PredictionContext(prefix, [])).ToArray();

        Assert.Contains(expectedSuggestion, suggestions);
    }

    [Theory]
    [InlineData("messy")]
    [InlineData("sample")]
    [InlineData("fingers")]
    [InlineData("corrections")]
    [InlineData("contractions")]
    public void SwipeSuggestionsIncludeExpandedCommonVocabulary(string word)
    {
        var engine = CreateEngine();

        var suggestions = engine.GetSwipeSuggestions(word, new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal(word, suggestions[0]);
    }

    [Theory]
    [InlineData("zebra")]
    [InlineData("swipe")]
    [InlineData("tomorrow")]
    public void SwipeSuggestionsIncludeBroadDictionaryWords(string word)
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSwipeSuggestions(word, new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal(word, suggestions[0]);
    }

    [Theory]
    [InlineData("keybord", "keyboard")]
    [InlineData("keybrd", "keyboard")]
    [InlineData("prdction", "prediction")]
    [InlineData("prdctn", "prediction")]
    [InlineData("tomorow", "tomorrow")]
    [InlineData("tmorw", "tomorrow")]
    [InlineData("instaltion", "installation")]
    [InlineData("instltn", "installation")]
    public void SwipeSuggestionsTolerateLongWordTraceErrors(string trace, string expectedSuggestion)
    {
        var engine = CreateEngine();
        var suggestions = engine.GetSwipeSuggestions(trace, new PredictionContext(string.Empty, [])).ToArray();

        Assert.Equal(expectedSuggestion, suggestions[0]);
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
