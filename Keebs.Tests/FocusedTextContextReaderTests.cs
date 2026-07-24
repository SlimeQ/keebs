namespace Keebs.Tests;

public sealed class FocusedTextContextReaderTests
{
    [Theory]
    [InlineData("xhtml")]
    [InlineData("XHTML")]
    [InlineData("xhtmlxhtml")]
    [InlineData(" html ")]
    public void RejectsKnownBrowserAccessibilityArtifacts(string text)
    {
        Assert.False(FocusedTextContextReader.IsUsableSeedText(text));
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("search query")]
    [InlineData("html5test")]
    public void AcceptsNormalSeedText(string text)
    {
        Assert.True(FocusedTextContextReader.IsUsableSeedText(text));
    }

    [Theory]
    [InlineData("xhtmlopenclaw", "openclaw")]
    [InlineData(" XHTML  search query", "search query")]
    [InlineData("xhtmlxhtmlhello ", "hello ")]
    [InlineData("prefix xhtml value", "prefix xhtml value")]
    [InlineData("hello\u200B world", "hello world")]
    [InlineData("hello\uFFFCworld", "hello world")]
    public void SanitizesAccessibilityMetadataWithoutDiscardingContext(string text, string expected)
    {
        Assert.Equal(expected, FocusedTextContextReader.SanitizeSeedText(text));
    }

    [Theory]
    [InlineData("search xhtmlquer", "search quer")]
    [InlineData("hello world xhtmlgoodb", "hello world goodb")]
    [InlineData("page text\nxhtmlsearch que", "page text\nsearch que")]
    [InlineData("hello\uFFFCxhtmlwor", "hello wor")]
    public void StripsArtifactTokenGluedToAWordAnywhereInTheContext(string text, string expected)
    {
        Assert.Equal(expected, FocusedTextContextReader.SanitizeSeedText(text));
    }

    [Theory]
    [InlineData("Search or enter address\u200Bgithub", "Search or enter address github")]
    [InlineData("hidden\uFFFCtyped", "hidden typed")]
    [InlineData("label\u0001value", "label value")]
    [InlineData("hidden\u200B typed", "hidden typed")]
    [InlineData("hidden \u200Btyped", "hidden typed")]
    [InlineData("caf\u200Ce\u200D", "cafe")]
    public void HiddenRunsBecomeAWordBoundaryInsteadOfMergingIntoTheTypedWord(string text, string expected)
    {
        Assert.Equal(expected, FocusedTextContextReader.SanitizeSeedText(text));
    }

    [Fact]
    public void HiddenTextIsKeptAsPredictionContext()
    {
        var session = new TextSession();

        session.SeedFromTextBeforeCaret(
            FocusedTextContextReader.SanitizeSeedText("xhtmlSearch with Google\u200Bgithu"));

        Assert.Equal("githu", session.Context.CurrentWord);
        Assert.Equal("google", session.Context.PreviousWord);
    }

    [Fact]
    public void UnavailableContextIsDistinctFromAnEmptyField()
    {
        Assert.False(FocusedTextContext.Unavailable.IsAvailable);
        Assert.True(FocusedTextContext.Empty.IsAvailable);
        Assert.Equal(string.Empty, FocusedTextContext.Empty.TextBeforeCaret);
    }
}
