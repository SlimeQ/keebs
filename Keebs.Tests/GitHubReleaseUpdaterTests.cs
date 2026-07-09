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

    [Fact]
    public async Task ReusesCachedResultWhenReleaseHasNotChanged()
    {
        var handler = new ConditionalReleaseHandler();
        var updater = new GitHubReleaseUpdater(new HttpClient(handler));

        var first = await updater.CheckForUpdateAsync();
        var second = await updater.CheckForUpdateAsync();

        Assert.Same(first, second);
        Assert.Equal(2, handler.RequestCount);
        Assert.True(handler.SawConditionalRequest);
    }

    [Fact]
    public void InstallerHandoffWaitsForExitThenInstallsAndRestarts()
    {
        var script = GitHubReleaseUpdater.CreateInstallerHandoffScript(
            @"C:\Updates\Keeb's Setup.msi",
            @"C:\Program Files\Keebs\Keebs.exe",
            4321);

        Assert.Contains("Wait-Process -Id 4321", script);
        Assert.Contains("Keeb''s Setup.msi", script);
        Assert.Contains("Start-Process -FilePath 'msiexec.exe'", script);
        Assert.Contains("-Wait -PassThru", script);
        Assert.Contains("Start-Process -FilePath 'C:\\Program Files\\Keebs\\Keebs.exe'", script);
    }

    private sealed class ConditionalReleaseHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public bool SawConditionalRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tag_name": "v99.0.0",
                          "html_url": "https://example.test/release",
                          "assets": []
                        }
                        """)
                };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"release-1\"");
                return Task.FromResult(response);
            }

            SawConditionalRequest = request.Headers.IfNoneMatch.Any(tag => tag.Tag == "\"release-1\"");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotModified));
        }
    }
}
