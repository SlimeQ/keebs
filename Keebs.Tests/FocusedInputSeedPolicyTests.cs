namespace Keebs.Tests;

public sealed class FocusedInputSeedPolicyTests
{
    [Fact]
    public void BackspaceSeedRepairsAWordCarryingHiddenMetadata()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("search que"),
            FocusedInputSeedKind.Backspace,
            currentWord: "xhtmlque");

        Assert.True(seed.ShouldApply);
        Assert.Equal("search que", seed.TextBeforeCaret);
    }

    [Fact]
    public void BackspaceSeedClearsTheSessionWhenTheFieldIsEmpty()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.Empty,
            FocusedInputSeedKind.Backspace,
            currentWord: "xhtml");

        Assert.True(seed.ShouldApply);
        Assert.Equal(string.Empty, seed.TextBeforeCaret);
    }

    [Fact]
    public void BackspaceSeedIgnoresAReadTakenBeforeTheDeleteLanded()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("hello worl"),
            FocusedInputSeedKind.Backspace,
            currentWord: "wor");

        Assert.False(seed.ShouldApply);
    }

    [Fact]
    public void BackspaceSeedAcceptsAReadThatMatchesTheLocalWord()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("hello wor"),
            FocusedInputSeedKind.Backspace,
            currentWord: "wor");

        Assert.True(seed.ShouldApply);
        Assert.Equal("hello wor", seed.TextBeforeCaret);
    }

    [Fact]
    public void UnreadableFieldKeepsExistingContextExceptOnFocusChange()
    {
        Assert.False(FocusedInputSeedPolicy
            .Resolve(FocusedTextContext.Unavailable, FocusedInputSeedKind.Backspace, "wor")
            .ShouldApply);
        Assert.False(FocusedInputSeedPolicy
            .Resolve(FocusedTextContext.Unavailable, FocusedInputSeedKind.CaretMove, "wor")
            .ShouldApply);

        var focusChange = FocusedInputSeedPolicy
            .Resolve(FocusedTextContext.Unavailable, FocusedInputSeedKind.FocusChange, "wor");

        Assert.True(focusChange.ShouldApply);
        Assert.Equal(string.Empty, focusChange.TextBeforeCaret);
    }

    [Fact]
    public void CaretMoveSeedAcceptsALongerWordThanTheSessionTracked()
    {
        var seed = FocusedInputSeedPolicy.Resolve(
            FocusedTextContext.FromSanitizedText("hello world"),
            FocusedInputSeedKind.CaretMove,
            currentWord: string.Empty);

        Assert.True(seed.ShouldApply);
        Assert.Equal("hello world", seed.TextBeforeCaret);
    }
}
