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
}
