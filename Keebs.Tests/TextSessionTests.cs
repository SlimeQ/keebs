namespace Keebs.Tests;

public sealed class TextSessionTests
{
    [Fact]
    public void SeedFromTextBeforeCaretUsesCurrentWordPrefix()
    {
        var session = new TextSession();

        session.SeedFromTextBeforeCaret("hello wor");

        Assert.Equal("wor", session.Context.CurrentWord);
        Assert.Equal("hello", session.Context.PreviousWord);
    }

    [Fact]
    public void SeedFromTextBeforeCaretUsesLastCommittedWordAfterBoundary()
    {
        var session = new TextSession();

        session.SeedFromTextBeforeCaret("hello world ");

        Assert.Equal(string.Empty, session.Context.CurrentWord);
        Assert.Equal("world", session.Context.PreviousWord);
    }

    [Fact]
    public void SeedFromTextBeforeCaretRequestsBoundaryAfterPunctuation()
    {
        var session = new TextSession();

        session.SeedFromTextBeforeCaret("hello.");

        Assert.True(session.NeedsWordBoundaryBeforeNextWord);
    }

    [Fact]
    public void SeedFromTextBeforeCaretDoesNotRequestBoundaryAfterWhitespace()
    {
        var session = new TextSession();

        session.SeedFromTextBeforeCaret("hello. ");

        Assert.False(session.NeedsWordBoundaryBeforeNextWord);
    }

    [Fact]
    public void TypeTextRequestsBoundaryAfterPunctuation()
    {
        var session = new TextSession();

        session.TypeText("hello.");

        Assert.True(session.NeedsWordBoundaryBeforeNextWord);
    }

    [Fact]
    public void TypeTextClearsBoundaryRequestAfterWhitespace()
    {
        var session = new TextSession();

        session.TypeText("hello. ");

        Assert.False(session.NeedsWordBoundaryBeforeNextWord);
    }

    [Fact]
    public void ResetPredictionContextClearsSeededPreviousWords()
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret("hello world ");

        session.ResetPredictionContext();

        Assert.Equal(string.Empty, session.Context.CurrentWord);
        Assert.Equal(string.Empty, session.Context.PreviousWord);
    }

    [Fact]
    public void BackspaceRemovesCharacterFromCurrentWord()
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret("hello worlq");

        session.Backspace();

        Assert.Equal("worl", session.Context.CurrentWord);
        Assert.Equal("hello", session.Context.PreviousWord);
    }

    [Fact]
    public void BackspaceAcrossSingleBoundaryRestoresPreviousWordAsCurrentPrefix()
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret("hello ");

        session.Backspace();

        Assert.Equal("hello", session.Context.CurrentWord);
        Assert.Equal(string.Empty, session.Context.PreviousWord);
    }

    [Fact]
    public void BackspaceThroughMultipleBoundariesWaitsUntilFinalBoundaryIsDeleted()
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret("hello   ");

        session.Backspace();
        session.Backspace();

        Assert.Equal(string.Empty, session.Context.CurrentWord);
        Assert.Equal("hello", session.Context.PreviousWord);

        session.Backspace();

        Assert.Equal("hello", session.Context.CurrentWord);
        Assert.Equal(string.Empty, session.Context.PreviousWord);
    }

    [Fact]
    public void BackspaceWordClearsCurrentWord()
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret("hello world");

        session.BackspaceWord();

        Assert.Equal(string.Empty, session.Context.CurrentWord);
        Assert.Equal("hello", session.Context.PreviousWord);
    }

    [Fact]
    public void BackspaceWordRemovesPreviousWordAfterBoundary()
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret("hello world ");

        session.BackspaceWord();

        Assert.Equal(string.Empty, session.Context.CurrentWord);
        Assert.Equal("hello", session.Context.PreviousWord);
    }

    [Theory]
    [InlineData("hello wor", "wor")]
    [InlineData("hello world ", "")]
    [InlineData("don't", "don't")]
    [InlineData("HELLO Wor", "wor")]
    [InlineData("", "")]
    public void ParseCurrentWordMatchesTheSeededCurrentWord(string textBeforeCaret, string expected)
    {
        var session = new TextSession();
        session.SeedFromTextBeforeCaret(textBeforeCaret);

        Assert.Equal(expected, TextSession.ParseCurrentWord(textBeforeCaret));
        Assert.Equal(session.Context.CurrentWord, TextSession.ParseCurrentWord(textBeforeCaret));
    }

    [Fact]
    public void LocalEditsAdvanceTheRevisionAndSeedingDoesNot()
    {
        var session = new TextSession();
        var initial = session.Revision;

        session.SeedFromTextBeforeCaret("hello wor");
        Assert.Equal(initial, session.Revision);

        session.TypeText("l");
        var afterTyping = session.Revision;
        Assert.True(afterTyping > initial);

        session.Backspace();
        var afterBackspace = session.Revision;
        Assert.True(afterBackspace > afterTyping);

        session.BackspaceWord();
        Assert.True(session.Revision > afterBackspace);
    }
}
