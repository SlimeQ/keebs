namespace Keebs.Tests;

public sealed class GitHubReleaseUpdaterTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3+abc123", "1.2.3")]
    [InlineData("v1.2.3-beta.1", "1.2.3")]
    public void ParsesReleaseTags(string tag, string expectedVersion)
    {
        Assert.True(GitHubReleaseUpdater.TryParseVersion(tag, out var version));
        Assert.Equal(new Version(expectedVersion), version);
    }

    [Theory]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("1.2.3", "1.2.3.0", 0)]
    [InlineData("1.2.3", "1.2.4", -1)]
    public void ComparesVersionsWithMissingPartsAsZero(string left, string right, int expectedSign)
    {
        var comparison = GitHubReleaseUpdater.CompareVersions(new Version(left), new Version(right));

        Assert.Equal(expectedSign, Math.Sign(comparison));
    }

    [Fact]
    public void SelectsWinX64MsiAsset()
    {
        var asset = GitHubReleaseUpdater.SelectInstallerAsset(
        [
            new GitHubReleaseAsset { Name = "Keebs.zip", BrowserDownloadUrl = "https://example.test/zip" },
            new GitHubReleaseAsset { Name = "Keebs-Setup-arm64.msi", BrowserDownloadUrl = "https://example.test/arm64" },
            new GitHubReleaseAsset { Name = "Keebs-Setup-win-x64.msi", BrowserDownloadUrl = "https://example.test/x64" }
        ]);

        Assert.NotNull(asset);
        Assert.Equal("https://example.test/x64", asset.BrowserDownloadUrl);
    }
}
