namespace Keebs.Tests;

public sealed class FocusedInputSeedPolicyTests
{
    [Fact]
    public void BackspaceSeedKeepsContextWhenTheProviderAnswersWithNothing()
    {
        var session = SessionAfterTyping("the visual");
        session.Backspace();
        session.Backspace();

        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.Empty,
            FocusedInputSeedKind.Backspace,
            session.Context);

        Assert.False(seed.ShouldApply);
        Assert.Equal("visu", session.Context.CurrentWord);
        Assert.Equal("the", session.Context.PreviousWord);
    }

    [Fact]
    public void BackspaceSeedAppliesTheRealTextBehindTheCaret()
    {
        var session = SessionAfterTyping("the visual");
        session.Backspace();
        session.Backspace();

        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("the visu"),
            FocusedInputSeedKind.Backspace,
            session.Context);

        Assert.True(seed.ShouldApply);
        Assert.Equal("the visu", seed.TextBeforeCaret);
    }

    [Fact]
    public void BackspaceSeedRepairsAWordCarryingHiddenMetadata()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("search que"),
            FocusedInputSeedKind.Backspace,
            new PredictionContext("xhtmlque", []));

        Assert.True(seed.ShouldApply);
        Assert.Equal("search que", seed.TextBeforeCaret);
    }

    [Fact]
    public void BackspaceSeedIgnoresAReadTakenBeforeTheDeleteLanded()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("hello worl"),
            FocusedInputSeedKind.Backspace,
            new PredictionContext("wor", ["hello"]));

        Assert.False(seed.ShouldApply);
    }

    [Fact]
    public void BackspaceSeedIgnoresTextFromAnUnrelatedField()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("Search with Google"),
            FocusedInputSeedKind.Backspace,
            new PredictionContext("visu", ["the"]));

        Assert.False(seed.ShouldApply);
    }

    [Fact]
    public void BackspaceSeedAcceptsAReadThatMatchesTheLocalWord()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("hello wor"),
            FocusedInputSeedKind.Backspace,
            new PredictionContext("wor", ["hello"]));

        Assert.True(seed.ShouldApply);
        Assert.Equal("hello wor", seed.TextBeforeCaret);
    }

    [Fact]
    public void BackspaceSeedAcceptsAnEmptyFieldOnceTheSessionIsAlsoEmpty()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.Empty,
            FocusedInputSeedKind.Backspace,
            new PredictionContext(string.Empty, []));

        Assert.True(seed.ShouldApply);
        Assert.Equal(string.Empty, seed.TextBeforeCaret);
    }

    [Fact]
    public void BackspaceSeedAcceptsACaretSittingAfterABoundary()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("the visual."),
            FocusedInputSeedKind.Backspace,
            new PredictionContext(string.Empty, ["the", "visual"]));

        Assert.True(seed.ShouldApply);
        Assert.Equal("the visual.", seed.TextBeforeCaret);
    }

    [Fact]
    public void UnreadableFieldKeepsExistingContextExceptOnFocusChange()
    {
        var session = new PredictionContext("wor", ["hello"]);

        Assert.False(FocusedInputSeedPolicy
            .Resolve(FocusedTextContext.Unavailable, FocusedInputSeedKind.Backspace, session)
            .ShouldApply);
        Assert.False(FocusedInputSeedPolicy
            .Resolve(FocusedTextContext.Unavailable, FocusedInputSeedKind.CaretMove, session)
            .ShouldApply);

        var focusChange = FocusedInputSeedPolicy
            .Resolve(FocusedTextContext.Unavailable, FocusedInputSeedKind.FocusChange, session);

        Assert.True(focusChange.ShouldApply);
        Assert.Equal(string.Empty, focusChange.TextBeforeCaret);
    }

    [Fact]
    public void CaretMoveSeedAcceptsALongerWordThanTheSessionTracked()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("hello world"),
            FocusedInputSeedKind.CaretMove,
            new PredictionContext(string.Empty, []));

        Assert.True(seed.ShouldApply);
        Assert.Equal("hello world", seed.TextBeforeCaret);
    }

    private static TextSession SessionAfterTyping(string text)
    {
        var session = new TextSession();
        session.TypeText(text);
        return session;
    }
}
