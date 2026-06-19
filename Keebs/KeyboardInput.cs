using System.Runtime.InteropServices;

namespace Keebs;

internal static class KeyboardInput
{
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;

    internal static int NativeInputSize => Marshal.SizeOf<Input>();

    internal static int ExpectedNativeInputSize => IntPtr.Size == 8 ? 40 : 28;

    internal static bool NativeInputLayoutMatchesWindows => NativeInputSize == ExpectedNativeInputSize;

    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new List<Input>(text.Length * 2);

        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        Send(inputs);
    }

    public static void SendVirtualKey(VirtualKey key)
    {
        SendVirtualKey((ushort)key);
    }

    public static void SendVirtualKey(ushort key)
    {
        Send(
        [
            CreateVirtualKeyInput(key, keyUp: false),
            CreateVirtualKeyInput(key, keyUp: true)
        ]);
    }

    public static void SendWithReleasedModifiers(Action sendAction)
    {
        var activeModifiers = new[]
            {
                VirtualKey.Control,
                VirtualKey.Shift,
                VirtualKey.Alt,
                VirtualKey.LeftWindows,
                VirtualKey.RightWindows
            }
            .Where(IsVirtualKeyDown)
            .ToArray();

        try
        {
            foreach (var modifier in activeModifiers)
            {
                SendVirtualKeyUp(modifier);
            }

            sendAction();
        }
        finally
        {
            for (var index = activeModifiers.Length - 1; index >= 0; index--)
            {
                SendVirtualKeyDown(activeModifiers[index]);
            }
        }
    }

    internal static bool IsVirtualKeyDown(VirtualKey key)
    {
        return (GetKeyState((int)key) & 0x8000) != 0;
    }

    private static void SendVirtualKeyDown(VirtualKey key)
    {
        Send([CreateVirtualKeyInput(key, keyUp: false)]);
    }

    private static void SendVirtualKeyUp(VirtualKey key)
    {
        Send([CreateVirtualKeyInput(key, keyUp: true)]);
    }

    public static void SendVirtualKeyChord(IReadOnlyList<VirtualKey> modifiers, VirtualKey key)
    {
        SendVirtualKeyChord(modifiers.Select(modifier => (ushort)modifier).ToArray(), (ushort)key);
    }

    public static void SendVirtualKeyChord(IReadOnlyList<ushort> modifiers, ushort key)
    {
        var inputs = new List<Input>((modifiers.Count * 2) + 2);

        foreach (var modifier in modifiers)
        {
            inputs.Add(CreateVirtualKeyInput(modifier, keyUp: false));
        }

        inputs.Add(CreateVirtualKeyInput(key, keyUp: false));
        inputs.Add(CreateVirtualKeyInput(key, keyUp: true));

        for (var index = modifiers.Count - 1; index >= 0; index--)
        {
            inputs.Add(CreateVirtualKeyInput(modifiers[index], keyUp: true));
        }

        Send(inputs);
    }

    private static Input CreateUnicodeInput(char character, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = KeyEventFUnicode | (keyUp ? KeyEventFKeyUp : 0)
                }
            }
        };
    }

    private static Input CreateVirtualKeyInput(VirtualKey key, bool keyUp)
    {
        return CreateVirtualKeyInput((ushort)key, keyUp);
    }

    private static Input CreateVirtualKeyInput(ushort key, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyEventFKeyUp : 0
                }
            }
        };
    }

    private static void Send(IReadOnlyList<Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Count} events. Win32 error: {error}.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        [FieldOffset(0)]
        public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInputData
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }
}
