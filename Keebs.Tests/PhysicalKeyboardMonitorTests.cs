namespace Keebs.Tests;

public sealed class PhysicalKeyboardMonitorTests
{
    [Fact]
    public void IgnoresInjectedKeyboardEvents()
    {
        Assert.True(PhysicalKeyboardMonitor.IsInjected(0x10));
        Assert.False(PhysicalKeyboardMonitor.IsInjected(0));
    }

    [Theory]
    [InlineData((uint)VirtualKey.A)]
    [InlineData((uint)VirtualKey.D1)]
    [InlineData((uint)VirtualKey.Back)]
    [InlineData((uint)VirtualKey.Space)]
    [InlineData((uint)VirtualKey.OemQuotes)]
    public void TreatsTextEditingKeysAsPredictionTriggers(uint virtualKey)
    {
        Assert.True(PhysicalKeyboardMonitor.IsPredictionTriggerKey(virtualKey));
    }

    [Theory]
    [InlineData((uint)VirtualKey.Shift)]
    [InlineData((uint)VirtualKey.Control)]
    [InlineData((uint)VirtualKey.Alt)]
    [InlineData((uint)VirtualKey.F1)]
    public void IgnoresModifierAndFunctionKeys(uint virtualKey)
    {
        Assert.False(PhysicalKeyboardMonitor.IsPredictionTriggerKey(virtualKey));
    }

    [Theory]
    [InlineData((uint)VirtualKey.A, false, false, "a")]
    [InlineData((uint)VirtualKey.A, true, false, "A")]
    [InlineData((uint)VirtualKey.A, false, true, "A")]
    [InlineData((uint)VirtualKey.A, true, true, "a")]
    [InlineData((uint)VirtualKey.D1, false, false, "1")]
    [InlineData((uint)VirtualKey.D1, true, false, "!")]
    [InlineData((uint)VirtualKey.OemQuotes, false, false, "'")]
    [InlineData((uint)VirtualKey.OemQuotes, true, false, "\"")]
    [InlineData((uint)VirtualKey.Space, false, false, " ")]
    public void TranslatesUsTextKeys(uint virtualKey, bool shift, bool capsLock, string expectedText)
    {
        Assert.Equal(expectedText, PhysicalKeyboardMonitor.GetTextForVirtualKey(
            virtualKey,
            shift,
            capsLock,
            shortcutModifierActive: false));
    }

    [Fact]
    public void DoesNotTranslateShortcutModifiedTextKeys()
    {
        Assert.Equal(string.Empty, PhysicalKeyboardMonitor.GetTextForVirtualKey(
            (uint)VirtualKey.A,
            shift: false,
            capsLock: false,
            shortcutModifierActive: true));
    }

    [Fact]
    public void TreatsControlSpaceAsAcceptFirstPredictionChord()
    {
        Assert.True(PhysicalKeyboardMonitor.IsAcceptFirstPredictionChord(
            (uint)VirtualKey.Space,
            control: true,
            alt: false,
            windows: false));
    }

    [Theory]
    [InlineData((uint)VirtualKey.Space, false, false, false)]
    [InlineData((uint)VirtualKey.Space, true, true, false)]
    [InlineData((uint)VirtualKey.Space, true, false, true)]
    [InlineData((uint)VirtualKey.A, true, false, false)]
    public void DoesNotTreatOtherChordsAsAcceptFirstPrediction(uint virtualKey, bool control, bool alt, bool windows)
    {
        Assert.False(PhysicalKeyboardMonitor.IsAcceptFirstPredictionChord(virtualKey, control, alt, windows));
    }
}
