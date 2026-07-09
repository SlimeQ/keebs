using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Keebs;

internal sealed class PhysicalKeyboardMonitor : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;
    private const uint LlkhfInjected = 0x00000010;
    private readonly LowLevelKeyboardProc _hookCallback;
    private readonly object _lifecycleLock = new();
    private bool _acceptPredictionChordActive;
    private ManualResetEventSlim? _hookReady;
    private IntPtr _hookHandle;
    private Exception? _hookStartupException;
    private Thread? _hookThread;
    private uint _hookThreadId;

    public PhysicalKeyboardMonitor()
    {
        _hookCallback = HookCallback;
    }

    public event EventHandler<PhysicalKeyPressedEventArgs>? TextInputKeyPressed;

    public void Start()
    {
        ManualResetEventSlim hookReady;

        lock (_lifecycleLock)
        {
            if (_hookThread is not null)
            {
                return;
            }

            _hookStartupException = null;
            hookReady = new ManualResetEventSlim();
            _hookReady = hookReady;
            _hookThread = new Thread(RunHookMessageLoop)
            {
                IsBackground = true,
                Name = "Keebs keyboard hook",
                Priority = ThreadPriority.AboveNormal
            };
            _hookThread.Start();
        }

        hookReady.Wait();

        if (_hookStartupException is { } startupException)
        {
            Dispose();
            throw new InvalidOperationException("Could not start the physical keyboard monitor.", startupException);
        }
    }

    public void Dispose()
    {
        ManualResetEventSlim? hookReady;
        Thread? hookThread;

        lock (_lifecycleLock)
        {
            hookReady = _hookReady;
            hookThread = _hookThread;
        }

        if (hookThread is null)
        {
            return;
        }

        hookReady?.Wait();
        var hookThreadId = _hookThreadId;
        if (hookThreadId != 0)
        {
            PostThreadMessage(hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (hookThread != Thread.CurrentThread)
        {
            hookThread.Join();
        }

        lock (_lifecycleLock)
        {
            if (!ReferenceEquals(_hookThread, hookThread))
            {
                return;
            }

            _hookThread = null;
            _hookReady?.Dispose();
            _hookReady = null;
        }
    }

    private void RunHookMessageLoop()
    {
        try
        {
            // Creating the queue before signaling readiness guarantees that Dispose can
            // reliably post WM_QUIT, even when the window closes immediately after startup.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _hookThreadId = GetCurrentThreadId();

            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
            _hookHandle = SetWindowsHookEx(WhKeyboardLowLevel, _hookCallback, moduleHandle, 0);
            if (_hookHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _hookReady?.Set();

            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        catch (Exception ex)
        {
            _hookStartupException = ex;
            _hookReady?.Set();
        }
        finally
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            _hookThreadId = 0;
        }
    }

    internal static bool IsInjected(uint flags)
    {
        return (flags & LlkhfInjected) != 0;
    }

    internal static bool IsPredictionTriggerKey(uint virtualKey)
    {
        return virtualKey is
                   >= 0x30 and <= 0x39 or
                   >= 0x41 and <= 0x5A or
                   (uint)VirtualKey.Back or
                   (uint)VirtualKey.Tab or
                   (uint)VirtualKey.Enter or
                   (uint)VirtualKey.Space or
                   (uint)VirtualKey.PageUp or
                   (uint)VirtualKey.PageDown or
                   (uint)VirtualKey.End or
                   (uint)VirtualKey.Home or
                   (uint)VirtualKey.Left or
                   (uint)VirtualKey.Up or
                   (uint)VirtualKey.Right or
                   (uint)VirtualKey.Down or
                   (uint)VirtualKey.Delete or
                   (uint)VirtualKey.OemSemicolon or
                   (uint)VirtualKey.OemPlus or
                   (uint)VirtualKey.OemComma or
                   (uint)VirtualKey.OemMinus or
                   (uint)VirtualKey.OemPeriod or
                   (uint)VirtualKey.OemQuestion or
                   (uint)VirtualKey.OemTilde or
                   (uint)VirtualKey.OemOpenBracket or
                   (uint)VirtualKey.OemPipe or
                   (uint)VirtualKey.OemCloseBracket or
                   (uint)VirtualKey.OemQuotes;
    }

    internal static string GetTextForVirtualKey(uint virtualKey, bool shift, bool capsLock, bool shortcutModifierActive)
    {
        if (shortcutModifierActive)
        {
            return string.Empty;
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            var character = (char)('a' + (virtualKey - 0x41));
            return shift ^ capsLock
                ? char.ToUpperInvariant(character).ToString()
                : character.ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            const string normalDigits = "0123456789";
            const string shiftedDigits = ")!@#$%^&*(";
            var index = (int)(virtualKey - 0x30);
            return (shift ? shiftedDigits[index] : normalDigits[index]).ToString();
        }

        if (virtualKey == (uint)VirtualKey.Space)
        {
            return " ";
        }

        return virtualKey switch
        {
            (uint)VirtualKey.OemSemicolon => shift ? ":" : ";",
            (uint)VirtualKey.OemPlus => shift ? "+" : "=",
            (uint)VirtualKey.OemComma => shift ? "<" : ",",
            (uint)VirtualKey.OemMinus => shift ? "_" : "-",
            (uint)VirtualKey.OemPeriod => shift ? ">" : ".",
            (uint)VirtualKey.OemQuestion => shift ? "?" : "/",
            (uint)VirtualKey.OemTilde => shift ? "~" : "`",
            (uint)VirtualKey.OemOpenBracket => shift ? "{" : "[",
            (uint)VirtualKey.OemPipe => shift ? "|" : "\\",
            (uint)VirtualKey.OemCloseBracket => shift ? "}" : "]",
            (uint)VirtualKey.OemQuotes => shift ? "\"" : "'",
            _ => string.Empty
        };
    }

    internal static bool IsAcceptFirstPredictionChord(uint virtualKey, bool control, bool alt, bool windows)
    {
        return virtualKey == (uint)VirtualKey.Space && control && !alt && !windows;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown || wParam == WmKeyUp || wParam == WmSysKeyUp))
        {
            var key = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            if (!IsInjected(key.Flags) && IsPredictionTriggerKey(key.VirtualKey))
            {
                if ((wParam == WmKeyUp || wParam == WmSysKeyUp) &&
                    key.VirtualKey == (uint)VirtualKey.Space &&
                    _acceptPredictionChordActive)
                {
                    _acceptPredictionChordActive = false;
                    return new IntPtr(1);
                }

                if (wParam != WmKeyDown && wParam != WmSysKeyDown)
                {
                    return CallNextHookEx(_hookHandle, code, wParam, lParam);
                }

                var shift = IsKeyDown((int)VirtualKey.Shift);
                var capsLock = IsKeyToggled((int)VirtualKey.CapsLock);
                var control = IsKeyDown((int)VirtualKey.Control);
                var alt = IsKeyDown((int)VirtualKey.Alt);
                var windows = IsKeyDown((int)VirtualKey.LeftWindows) ||
                              IsKeyDown((int)VirtualKey.RightWindows);
                var shortcutModifierActive = control || alt || windows;
                var acceptPredictionChord = IsAcceptFirstPredictionChord(key.VirtualKey, control, alt, windows);
                if (acceptPredictionChord && _acceptPredictionChordActive)
                {
                    return new IntPtr(1);
                }

                var text = GetTextForVirtualKey(key.VirtualKey, shift, capsLock, shortcutModifierActive);
                var args = new PhysicalKeyPressedEventArgs(
                    key.VirtualKey,
                    text,
                    acceptPredictionChord,
                    shift,
                    control,
                    alt,
                    windows);
                try
                {
                    TextInputKeyPressed?.Invoke(this, args);
                }
                catch (Exception)
                {
                    // Never allow subscriber failures to escape into Windows' global input path.
                    return CallNextHookEx(_hookHandle, code, wParam, lParam);
                }

                if (args.Handled)
                {
                    _acceptPredictionChordActive = acceptPredictionChord;
                    return new IntPtr(1);
                }
            }
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsKeyToggled(int virtualKey)
    {
        return (GetKeyState(virtualKey) & 0x0001) != 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, IntPtr windowHandle, uint minimumMessage, uint maximumMessage);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }
}

internal sealed class PhysicalKeyPressedEventArgs(
    uint virtualKey,
    string text,
    bool isAcceptFirstPredictionChord,
    bool shift = false,
    bool control = false,
    bool alt = false,
    bool windows = false) : EventArgs
{
    public uint VirtualKey { get; } = virtualKey;

    public string Text { get; } = text;

    public bool IsAcceptFirstPredictionChord { get; } = isAcceptFirstPredictionChord;

    public bool Shift { get; } = shift;

    public bool Control { get; } = control;

    public bool Alt { get; } = alt;

    public bool Windows { get; } = windows;

    public bool Handled { get; set; }
}
