namespace Keebs.Tests;

public sealed class KeyboardInputInteropTests
{
    [Fact]
    public void NativeInputStructSizeMatchesWindowsLayout()
    {
        Assert.True(
            KeyboardInput.NativeInputLayoutMatchesWindows,
            $"Expected INPUT size {KeyboardInput.ExpectedNativeInputSize}, got {KeyboardInput.NativeInputSize}.");
    }

    [Fact]
    public void NativeInputStructSizeUsesArchitectureSpecificWin32Size()
    {
        var expectedSize = Environment.Is64BitProcess ? 40 : 28;

        Assert.Equal(expectedSize, KeyboardInput.NativeInputSize);
    }
}
