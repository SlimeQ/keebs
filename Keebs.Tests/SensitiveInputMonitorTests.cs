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
    [InlineData("Message #general typing in channel")]
    [InlineData("chat-input typing-textarea slateTextArea")]
    [InlineData("Discord message input placeholder")]
    public void DoesNotTreatDiscordTypingMetadataAsSensitive(string metadata)
    {
        Assert.False(SensitiveInputMonitor.IsSensitiveMetadata(metadata));
    }

    [Theory]
    [InlineData("API token")]
    [InlineData("personal access token")]
    [InlineData("password")]
    [InlineData("verification code")]
    [InlineData("PIN")]
    [InlineData("enter OTP")]
    [InlineData("card CVV")]
    public void TreatsCredentialMetadataAsSensitive(string metadata)
    {
        Assert.True(SensitiveInputMonitor.IsSensitiveMetadata(metadata));
    }

    [Theory]
    [InlineData("quincy@example.com's password:")]
    [InlineData("Enter passphrase for key '/home/quincy/.ssh/id_ed25519':")]
    [InlineData("Verification code:")]
    public void TreatsTerminalCredentialPromptsAsSensitiveContext(string textBeforeCaret)
    {
        Assert.True(SensitiveInputMonitor.IsSensitiveTextContext(textBeforeCaret));
    }

    [Theory]
    [InlineData("quincy@box:~/repo$ ")]
    [InlineData("building password manager docs")]
    [InlineData("the password field bug is fixed")]
    public void DoesNotTreatNormalTerminalTextAsSensitiveContext(string textBeforeCaret)
    {
        Assert.False(SensitiveInputMonitor.IsSensitiveTextContext(textBeforeCaret));
    }

    [Theory]
    [InlineData("ssh robokrabs@robokrabs")]
    [InlineData("ssh.exe -p 2222 robokrabs@robokrabs")]
    [InlineData("scp file.txt robokrabs@robokrabs:/tmp/file.txt")]
    [InlineData("sftp robokrabs@robokrabs")]
    [InlineData("sudo systemctl restart keebs")]
    public void TreatsCredentialPromptCommandsAsSensitive(string commandLine)
    {
        Assert.True(SensitiveInputMonitor.IsCredentialPromptCommand(commandLine));
    }

    [Theory]
    [InlineData("echo ssh robokrabs@robokrabs")]
    [InlineData("git status")]
    [InlineData("building sudo docs")]
    public void DoesNotTreatNormalCommandsAsCredentialPromptCommands(string commandLine)
    {
        Assert.False(SensitiveInputMonitor.IsCredentialPromptCommand(commandLine));
    }
}
