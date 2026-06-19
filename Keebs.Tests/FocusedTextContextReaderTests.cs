namespace Keebs.Tests;

public sealed class FocusedTextContextReaderTests
{
    [Theory]
    [InlineData("xhtml")]
    [InlineData("XHTML")]
    [InlineData(" html ")]
    public void RejectsKnownBrowserAccessibilityArtifacts(string text)
    {
        Assert.False(FocusedTextContextReader.IsUsableSeedText(text));
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("search query")]
    public void AcceptsNormalSeedText(string text)
    {
        Assert.True(FocusedTextContextReader.IsUsableSeedText(text));
    }
}
