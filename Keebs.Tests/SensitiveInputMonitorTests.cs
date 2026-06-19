namespace Keebs.Tests;

public sealed class SensitiveInputMonitorTests
{
    [Fact]
    public void DoesNotTreatCssTokenClassAsSensitiveMetadata()
    {
        const string metadata = "text-size-chat ProseMirror text-token-foreground overflow-y-auto";

        Assert.False(SensitiveInputMonitor.IsSensitiveMetadata(metadata));
        Assert.False(SensitiveInputMonitor.LooksLikeNativeClassName(metadata));
    }

    [Theory]
    [InlineData("API token")]
    [InlineData("personal access token")]
    [InlineData("password")]
    [InlineData("verification code")]
    public void TreatsCredentialMetadataAsSensitive(string metadata)
    {
        Assert.True(SensitiveInputMonitor.IsSensitiveMetadata(metadata));
    }
}
