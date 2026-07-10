using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Keebs;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly TimeSpan FocusedContextReadTimeout = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan AutomaticUpdateCheckInterval = TimeSpan.FromHours(1);
    private const double SwipeTapThreshold = 20;
    private const double SwipeActivationKeyDistance = 1.2;
    private const int MinimumSwipeLetters = 2;
    private const int MinimumActiveSwipeLetters = 3;
    private const int SwipeCandidateLimit = 192;
    private const double SwipeMaximumGeometryScore = 5.0;
    private const double SwipeStrongGeometryScore = 0.55;
    private const double SwipeMinimumScoreMargin = 0.14;
    private const double SwipeContextScoreBonus = 4.0;
    private readonly ObservableCollection<string> _suggestions = [];
    private readonly TextPredictionEngine _predictionEngine;
    private readonly TextSession _textSession = new();
    private readonly SensitiveInputMonitor _sensitiveInputMonitor = new();
    private readonly PhysicalKeyboardMonitor _physicalKeyboardMonitor = new();
    private readonly GitHubReleaseUpdater _releaseUpdater = new();
    private readonly SwipeTraceRecorder _swipeTraceRecorder = new();
    private TypingTestWindow? _typingTestWindow;
    private readonly bool _startFocusMonitors;
    private readonly bool _checkForUpdates;
    private DispatcherTimer? _updateCheckTimer;
    private readonly List<char> _swipeLetters = [];
    private readonly List<Point> _swipePoints = [];
    private readonly StringBuilder _inputLineBuffer = new();
    private static readonly FontFamily TextKeyFontFamily = new("Bahnschrift SemiCondensed, Segoe UI");
    private static readonly FontFamily IconKeyFontFamily = new("Segoe UI Symbol, Segoe UI");
    private TouchDevice? _activeTouchDevice;
    private bool _shift;
    private bool _capsLock;
    private bool _control;
    private bool _alt;
    private bool _windows;
    private bool _predictionsEnabled = true;
    private bool _learningEnabled = true;
    private bool _updateAvailable;
    private bool _updateOperationInProgress;
    private volatile bool _focusedTextInputActive = true;
    private volatile bool _focusedTextContextSensitive;
    private bool _physicalSelectionActive;
    private bool _pointerGestureActive;
    private bool _swipeGestureActive;
    private volatile bool _focusedContextAllowEmptyContext;
    private int _focusedContextReadInFlight;
    private int _focusedContextRequestId;
    private int _updateCheckInFlight;
    private Point _pointerDownPosition;
    private KeySpec? _pointerDownKey;
    private double _keyFontSize = 14;
    private double _statusFontSize = 12;
    private double _statusDotSize = 7;
    private Thickness _outerMargin = new(12);
    private Thickness _shellPadding = new(10);
    private Thickness _deckPadding = new(7);
    private Brush _statusAccentBrush = new SolidColorBrush(Color.FromRgb(109, 196, 137));
    private string _footerHintText = "Predictions are local. Sensitive fields get raw key input only.";

    public MainWindow()
        : this(new TextPredictionEngine(), startFocusMonitors: true, checkForUpdates: true)
    {
    }

    internal MainWindow(TextPredictionEngine predictionEngine)
        : this(predictionEngine, startFocusMonitors: true, checkForUpdates: false)
    {
    }

    internal MainWindow(TextPredictionEngine predictionEngine, bool startFocusMonitors)
        : this(predictionEngine, startFocusMonitors, checkForUpdates: false)
    {
    }

    private MainWindow(TextPredictionEngine predictionEngine, bool startFocusMonitors, bool checkForUpdates)
    {
        _predictionEngine = predictionEngine;
        _startFocusMonitors = startFocusMonitors;
        _checkForUpdates = checkForUpdates;
        InitializeComponent();
        VersionLabel.Text = VersionLabelText;
        DataContext = this;
        SuggestionStrip.ItemsSource = _suggestions;
        BuildKeyboard();
        KeyboardGrid.PreviewMouseLeftButtonDown += KeyboardGrid_PreviewMouseLeftButtonDown;
        KeyboardGrid.PreviewMouseMove += KeyboardGrid_PreviewMouseMove;
        KeyboardGrid.PreviewMouseLeftButtonUp += KeyboardGrid_PreviewMouseLeftButtonUp;
        KeyboardGrid.TouchDown += KeyboardGrid_TouchDown;
        KeyboardGrid.TouchMove += KeyboardGrid_TouchMove;
        KeyboardGrid.TouchUp += KeyboardGrid_TouchUp;

        _sensitiveInputMonitor.FocusChanged += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                RefreshFocusedInputState();
            });
        _physicalKeyboardMonitor.TextInputKeyPressed += (_, key) =>
        {
            if (!ShouldProcessPhysicalKeyboardEvent(key))
            {
                return;
            }

            if (key.IsAcceptFirstPredictionChord)
            {
                key.Handled = true;
                Dispatcher.BeginInvoke(() =>
                {
                    AcceptFirstSuggestionFromShortcut();
                });
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                ApplyPhysicalKeyToPredictionSession(key);
            });
        };
        Loaded += (_, _) =>
        {
            if (_startFocusMonitors)
            {
                _sensitiveInputMonitor.Start();
                _physicalKeyboardMonitor.Start();
                RefreshFocusedInputState();
            }

            if (_checkForUpdates)
            {
                StartAutomaticUpdateChecks();
            }

            UpdateScale();
            ScheduleScaleUpdate();
        };
        Closed += (_, _) =>
        {
            _updateCheckTimer?.Stop();
            _physicalKeyboardMonitor.Dispose();
            _sensitiveInputMonitor.Dispose();
        };
        SizeChanged += (_, _) =>
        {
            UpdateScale();
            ScheduleScaleUpdate();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string VersionLabelText => $"v{GitHubReleaseUpdater.CurrentVersion}";

    public double KeyFontSize
    {
        get => _keyFontSize;
        private set => SetField(ref _keyFontSize, value);
    }

    public double StatusFontSize
    {
        get => _statusFontSize;
        private set => SetField(ref _statusFontSize, value);
    }

    public double StatusDotSize
    {
        get => _statusDotSize;
        private set => SetField(ref _statusDotSize, value);
    }

    public Thickness OuterMargin
    {
        get => _outerMargin;
        private set => SetField(ref _outerMargin, value);
    }

    public Thickness ShellPadding
    {
        get => _shellPadding;
        private set => SetField(ref _shellPadding, value);
    }

    public Thickness DeckPadding
    {
        get => _deckPadding;
        private set => SetField(ref _deckPadding, value);
    }

    public Brush StatusAccentBrush
    {
        get => _statusAccentBrush;
        private set => SetField(ref _statusAccentBrush, value);
    }

    public string FooterHintText
    {
        get => _footerHintText;
        private set => SetField(ref _footerHintText, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
            NativeMethods.MakeNoActivate(source.Handle);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmMouseActivate = 0x0021;
        const int maNoActivate = 3;

        if (msg == wmMouseActivate)
        {
            handled = true;
            return new IntPtr(maNoActivate);
        }

        return IntPtr.Zero;
    }

    private void Chassis_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ResetPointerGesture();
        NativeMethods.BeginWindowDrag(handle);
        e.Handled = true;
    }

    internal static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void BuildKeyboard()
    {
        KeyboardGrid.Children.Clear();
        KeyboardGrid.RowDefinitions.Clear();

        for (var index = 0; index < 6; index++)
        {
            KeyboardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        AddRow(0,
        [
            Slot(Key("Escape", "Esc", utility: true, virtualKey: VirtualKey.Escape)),
            Gap(0.75),
            Slot(Key("F1", "F1", utility: true, virtualKey: VirtualKey.F1)),
            Slot(Key("F2", "F2", utility: true, virtualKey: VirtualKey.F2)),
            Slot(Key("F3", "F3", utility: true, virtualKey: VirtualKey.F3)),
            Slot(Key("F4", "F4", utility: true, virtualKey: VirtualKey.F4)),
            Gap(0.35),
            Slot(Key("F5", "F5", utility: true, virtualKey: VirtualKey.F5)),
            Slot(Key("F6", "F6", utility: true, virtualKey: VirtualKey.F6)),
            Slot(Key("F7", "F7", utility: true, virtualKey: VirtualKey.F7)),
            Slot(Key("F8", "F8", utility: true, virtualKey: VirtualKey.F8)),
            Gap(0.35),
            Slot(Key("F9", "F9", utility: true, virtualKey: VirtualKey.F9)),
            Slot(Key("F10", "F10", utility: true, virtualKey: VirtualKey.F10)),
            Slot(Key("F11", "F11", utility: true, virtualKey: VirtualKey.F11)),
            Slot(Key("F12", "F12", utility: true, virtualKey: VirtualKey.F12)),
            Gap(0.75),
            Slot(Key("PrintScreen", "Prt\nSc", utility: true, virtualKey: VirtualKey.PrintScreen)),
            Slot(Key("ScrollLock", "Scr\nLk", utility: true, virtualKey: VirtualKey.ScrollLock)),
            Slot(Key("Pause", "Pause", utility: true, virtualKey: VirtualKey.Pause))
        ]);

        AddRow(1,
        [
            Slot(Key("Backquote", "`\n~", text: "`", shiftedText: "~", virtualKey: VirtualKey.OemTilde)),
            Slot(Key("D1", "1\n!", text: "1", shiftedText: "!", virtualKey: VirtualKey.D1)),
            Slot(Key("D2", "2\n@", text: "2", shiftedText: "@", virtualKey: VirtualKey.D2)),
            Slot(Key("D3", "3\n#", text: "3", shiftedText: "#", virtualKey: VirtualKey.D3)),
            Slot(Key("D4", "4\n$", text: "4", shiftedText: "$", virtualKey: VirtualKey.D4)),
            Slot(Key("D5", "5\n%", text: "5", shiftedText: "%", virtualKey: VirtualKey.D5)),
            Slot(Key("D6", "6\n^", text: "6", shiftedText: "^", virtualKey: VirtualKey.D6)),
            Slot(Key("D7", "7\n&", text: "7", shiftedText: "&", virtualKey: VirtualKey.D7)),
            Slot(Key("D8", "8\n*", text: "8", shiftedText: "*", virtualKey: VirtualKey.D8)),
            Slot(Key("D9", "9\n(", text: "9", shiftedText: "(", virtualKey: VirtualKey.D9)),
            Slot(Key("D0", "0\n)", text: "0", shiftedText: ")", virtualKey: VirtualKey.D0)),
            Slot(Key("Minus", "-\n_", text: "-", shiftedText: "_", virtualKey: VirtualKey.OemMinus)),
            Slot(Key("Equals", "=\n+", text: "=", shiftedText: "+", virtualKey: VirtualKey.OemPlus)),
            Slot(Key("Backspace", "Backspace", 2, utility: true, virtualKey: VirtualKey.Back)),
            Gap(0.75),
            Slot(Key("Insert", "Insert", utility: true, virtualKey: VirtualKey.Insert)),
            Slot(Key("Home", "Home", utility: true, virtualKey: VirtualKey.Home)),
            Slot(Key("PageUp", "Page\nUp", utility: true, virtualKey: VirtualKey.PageUp))
        ]);

        AddRow(2,
        [
            Slot(Key("Tab", "Tab", 1.5, utility: true, virtualKey: VirtualKey.Tab)),
            Slot(Key("Q", "Q", text: "q", virtualKey: VirtualKey.Q)),
            Slot(Key("W", "W", text: "w", virtualKey: VirtualKey.W)),
            Slot(Key("E", "E", text: "e", virtualKey: VirtualKey.E)),
            Slot(Key("R", "R", text: "r", virtualKey: VirtualKey.R)),
            Slot(Key("T", "T", text: "t", virtualKey: VirtualKey.T)),
            Slot(Key("Y", "Y", text: "y", virtualKey: VirtualKey.Y)),
            Slot(Key("U", "U", text: "u", virtualKey: VirtualKey.U)),
            Slot(Key("I", "I", text: "i", virtualKey: VirtualKey.I)),
            Slot(Key("O", "O", text: "o", virtualKey: VirtualKey.O)),
            Slot(Key("P", "P", text: "p", virtualKey: VirtualKey.P)),
            Slot(Key("OpenBracket", "[\n{", text: "[", shiftedText: "{", virtualKey: VirtualKey.OemOpenBracket)),
            Slot(Key("CloseBracket", "]\n}", text: "]", shiftedText: "}", virtualKey: VirtualKey.OemCloseBracket)),
            Slot(Key("Backslash", "\\\n|", 1.5, text: "\\", shiftedText: "|", virtualKey: VirtualKey.OemPipe)),
            Gap(0.75),
            Slot(Key("Delete", "Delete", utility: true, virtualKey: VirtualKey.Delete)),
            Slot(Key("End", "End", utility: true, virtualKey: VirtualKey.End)),
            Slot(Key("PageDown", "Page\nDn", utility: true, virtualKey: VirtualKey.PageDown))
        ]);

        AddRow(3,
        [
            Slot(Key("CapsLock", "Caps", 1.75, utility: true, virtualKey: VirtualKey.CapsLock)),
            Slot(Key("A", "A", text: "a", virtualKey: VirtualKey.A)),
            Slot(Key("S", "S", text: "s", virtualKey: VirtualKey.S)),
            Slot(Key("D", "D", text: "d", virtualKey: VirtualKey.D)),
            Slot(Key("F", "F", text: "f", virtualKey: VirtualKey.F)),
            Slot(Key("G", "G", text: "g", virtualKey: VirtualKey.G)),
            Slot(Key("H", "H", text: "h", virtualKey: VirtualKey.H)),
            Slot(Key("J", "J", text: "j", virtualKey: VirtualKey.J)),
            Slot(Key("K", "K", text: "k", virtualKey: VirtualKey.K)),
            Slot(Key("L", "L", text: "l", virtualKey: VirtualKey.L)),
            Slot(Key("Semicolon", ";\n:", text: ";", shiftedText: ":", virtualKey: VirtualKey.OemSemicolon)),
            Slot(Key("Quote", "'\n\"", text: "'", shiftedText: "\"", virtualKey: VirtualKey.OemQuotes)),
            Slot(Key("Enter", "Enter", 2.25, utility: true, virtualKey: VirtualKey.Enter)),
            Gap(3.75)
        ]);

        AddRow(4,
        [
            Slot(Key("ShiftLeft", "Shift", 2.25, utility: true, virtualKey: VirtualKey.Shift)),
            Slot(Key("Z", "Z", text: "z", virtualKey: VirtualKey.Z)),
            Slot(Key("X", "X", text: "x", virtualKey: VirtualKey.X)),
            Slot(Key("C", "C", text: "c", virtualKey: VirtualKey.C)),
            Slot(Key("V", "V", text: "v", virtualKey: VirtualKey.V)),
            Slot(Key("B", "B", text: "b", virtualKey: VirtualKey.B)),
            Slot(Key("N", "N", text: "n", virtualKey: VirtualKey.N)),
            Slot(Key("M", "M", text: "m", virtualKey: VirtualKey.M)),
            Slot(Key("Comma", ",\n<", text: ",", shiftedText: "<", virtualKey: VirtualKey.OemComma)),
            Slot(Key("Period", ".\n>", text: ".", shiftedText: ">", virtualKey: VirtualKey.OemPeriod)),
            Slot(Key("Slash", "/\n?", text: "/", shiftedText: "?", virtualKey: VirtualKey.OemQuestion)),
            Slot(Key("ShiftRight", "Shift", 2.75, utility: true, virtualKey: VirtualKey.Shift)),
            Gap(0.75),
            Gap(1),
            Slot(Key("Up", "Up", utility: true, virtualKey: VirtualKey.Up)),
            Gap(1)
        ]);

        AddRow(5,
        [
            Slot(Key("CtrlLeft", "Ctrl", 1.25, utility: true, virtualKey: VirtualKey.Control)),
            Slot(Key("WinLeft", "Win", 1.25, utility: true, virtualKey: VirtualKey.LeftWindows)),
            Slot(Key("AltLeft", "Alt", 1.25, utility: true, virtualKey: VirtualKey.Alt)),
            Slot(Key("Space", "Space", 6.25, text: " ", virtualKey: VirtualKey.Space)),
            Slot(Key("AltRight", "Alt", 1.25, utility: true, virtualKey: VirtualKey.Alt)),
            Slot(Key("WinRight", "Win", 1.25, utility: true, virtualKey: VirtualKey.LeftWindows)),
            Slot(Key("Menu", "Menu", 1.25, utility: true, virtualKey: VirtualKey.Applications)),
            Slot(Key("CtrlRight", "Ctrl", 1.25, utility: true, virtualKey: VirtualKey.Control)),
            Gap(0.75),
            Slot(Key("Left", "Left", utility: true, virtualKey: VirtualKey.Left)),
            Slot(Key("Down", "Down", utility: true, virtualKey: VirtualKey.Down)),
            Slot(Key("Right", "Right", utility: true, virtualKey: VirtualKey.Right))
        ]);
    }

    private void AddRow(int rowIndex, IReadOnlyList<KeySlot> slots)
    {
        var row = new Grid
        {
            Tag = rowIndex,
            Margin = GetRowMargin(rowIndex)
        };

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(slot.Width, GridUnitType.Star)
            });

            if (slot.Key is null)
            {
                continue;
            }

            var styleName = IsAccentKey(slot.Key)
                ? "AccentButton"
                : slot.Key.IsUtility
                    ? "UtilityButton"
                    : "KeyboardButton";
            var button = new Button
            {
                Content = GetKeyDisplay(slot.Key),
                Tag = slot.Key,
                FontFamily = GetKeyFontFamily(slot.Key),
                Style = (Style)FindResource(styleName)
            };

            button.Click += Key_Click;
            button.PreviewMouseDown += Key_PreviewMouseDown;
            Grid.SetColumn(button, index);
            row.Children.Add(button);
        }

        Grid.SetRow(row, rowIndex);
        KeyboardGrid.Children.Add(row);
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: KeySpec key })
        {
            return;
        }

        PressKey(key);
    }

    private void Key_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: KeySpec key } || GetPointerChordModifier(e.ChangedButton) is not { } modifier)
        {
            return;
        }

        e.Handled = true;
        PressKeyWithPointerModifier(key, modifier);
    }

    internal static VirtualKey? GetPointerChordModifier(MouseButton button)
    {
        return button switch
        {
            MouseButton.Right => VirtualKey.Control,
            MouseButton.Middle => VirtualKey.Alt,
            _ => null
        };
    }

    private void PressKeyWithPointerModifier(KeySpec key, VirtualKey pointerModifier)
    {
        if (key.VirtualKey is not { } virtualKey)
        {
            return;
        }

        var modifiers = GetActiveModifiers().ToList();
        if (!modifiers.Contains(pointerModifier))
        {
            modifiers.Add(pointerModifier);
        }

        modifiers.Remove(virtualKey);

        try
        {
            KeyboardInput.SendVirtualKeyChord(modifiers, virtualKey);
        }
        catch (InvalidOperationException ex)
        {
            ShowInputError(ex);
            return;
        }

        ResetTransientModifiers();
    }

    private void PressKey(KeySpec key)
    {
        try
        {
            switch (key.Id)
            {
                case "ShiftLeft":
                case "ShiftRight":
                    ToggleModifier(ref _shift);
                    return;
                case "CtrlLeft":
                case "CtrlRight":
                    ToggleModifier(ref _control);
                    return;
                case "AltLeft":
                case "AltRight":
                    ToggleModifier(ref _alt);
                    return;
                case "WinLeft":
                case "WinRight":
                    ToggleModifier(ref _windows);
                    return;
                case "CapsLock":
                    _capsLock = !_capsLock;
                    KeyboardInput.SendVirtualKey(VirtualKey.CapsLock);
                    RefreshKeyLabels();
                    return;
                case "Backspace":
                    SendVirtualKey(key);
                    if (PredictionsSuppressed)
                    {
                        break;
                    }

                    _textSession.Backspace();
                    TrackInputBackspace();
                    ScheduleFocusedInputResync(allowEmptyContext: false);
                    break;
                case "Tab":
                    SendVirtualKey(key);
                    var wasTabSuppressed = PredictionsSuppressed;
                    TrackSubmittedInputLine();
                    if (wasTabSuppressed)
                    {
                        ClearSensitiveTextContextAfterSubmission();
                        break;
                    }

                    if (PredictionsSuppressed)
                    {
                        break;
                    }

                    LearnTypedText(_textSession.CommitBoundary());
                    break;
                case "Enter":
                    SendVirtualKey(key);
                    var wasEnterSuppressed = PredictionsSuppressed;
                    TrackSubmittedInputLine();
                    if (wasEnterSuppressed)
                    {
                        ClearSensitiveTextContextAfterSubmission();
                        break;
                    }

                    if (PredictionsSuppressed)
                    {
                        break;
                    }

                    LearnTypedText(_textSession.CommitBoundary());
                    break;
                case "Space":
                    SendTextKey(key);
                    break;
                default:
                    if (key.Text is not null)
                    {
                        SendTextKey(key);
                    }
                    else
                    {
                        SendVirtualKey(key);
                        LearnTypedText(_textSession.CommitBoundary());
                    }

                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            ShowInputError(ex);
            return;
        }

        ResetTransientModifiers();
        RefreshSuggestions();
    }

    private void KeyboardGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginPointerGesture(e.GetPosition(KeyboardGrid), e);
    }

    private void KeyboardGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerGestureActive || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ContinuePointerGesture(e.GetPosition(KeyboardGrid), e);
    }

    private void KeyboardGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndPointerGesture(e.GetPosition(KeyboardGrid), e);
    }

    private void KeyboardGrid_TouchDown(object? sender, TouchEventArgs e)
    {
        if (_activeTouchDevice is not null)
        {
            return;
        }

        _activeTouchDevice = e.TouchDevice;
        KeyboardGrid.CaptureTouch(e.TouchDevice);
        BeginPointerGesture(e.GetTouchPoint(KeyboardGrid).Position, e);
    }

    private void KeyboardGrid_TouchMove(object? sender, TouchEventArgs e)
    {
        if (e.TouchDevice != _activeTouchDevice)
        {
            return;
        }

        ContinuePointerGesture(e.GetTouchPoint(KeyboardGrid).Position, e);
    }

    private void KeyboardGrid_TouchUp(object? sender, TouchEventArgs e)
    {
        if (e.TouchDevice != _activeTouchDevice)
        {
            return;
        }

        EndPointerGesture(e.GetTouchPoint(KeyboardGrid).Position, e);
        KeyboardGrid.ReleaseTouchCapture(e.TouchDevice);
        _activeTouchDevice = null;
    }

    private void BeginPointerGesture(Point position, RoutedEventArgs e)
    {
        var key = GetKeyAt(position);
        if (key is null)
        {
            return;
        }

        _pointerGestureActive = true;
        _swipeGestureActive = false;
        _pointerDownPosition = position;
        _pointerDownKey = key;
        _swipeLetters.Clear();
        _swipePoints.Clear();
        SwipeTrailLine.Points.Clear();
        AddSwipePoint(position);
        AddSwipeKey(key);
        KeyboardGrid.CaptureMouse();
        e.Handled = true;
    }

    private void ContinuePointerGesture(Point position, RoutedEventArgs e)
    {
        if (!_pointerGestureActive)
        {
            return;
        }

        AddSwipePoint(position);
        AddSwipeKeyAt(position);

        if (!_swipeGestureActive && ShouldActivateSwipeGesture(position))
        {
            _swipeGestureActive = true;
            SwipeTrailLine.Opacity = 0.82;
            RefreshSwipeSuggestions();
        }

        if (_swipeGestureActive)
        {
            RefreshSwipeSuggestions();
        }

        e.Handled = true;
    }

    private void EndPointerGesture(Point position, RoutedEventArgs e)
    {
        if (!_pointerGestureActive)
        {
            return;
        }

        if (_swipeGestureActive)
        {
            AddSwipePoint(position);
            AddSwipeKeyAt(position);
        }

        KeyboardGrid.ReleaseMouseCapture();

        var wasSwipeGesture = _swipeGestureActive;
        if (_swipeGestureActive)
        {
            var committedSwipe = TryCommitSwipe();
            if (!committedSwipe && ShouldFallbackSwipeToTap(position) && _pointerDownKey is { } fallbackKey)
            {
                PressKey(fallbackKey);
            }
        }
        else if (_pointerDownKey is { } pointerDownKey)
        {
            PressKey(pointerDownKey);
        }

        ResetPointerGesture();
        if (wasSwipeGesture)
        {
            RefreshSuggestions();
        }

        e.Handled = true;
    }

    private bool ShouldFallbackSwipeToTap(Point position)
    {
        if (_pointerDownKey is null ||
            _swipeLetters.Distinct().Count() >= MinimumSwipeLetters)
        {
            return false;
        }

        return GetKeyAt(position)?.Id == _pointerDownKey.Id;
    }

    private bool ShouldActivateSwipeGesture(Point position)
    {
        if (_swipeLetters.Distinct().Count() < MinimumActiveSwipeLetters)
        {
            return false;
        }

        var keyUnit = GetAverageLetterKeySize();
        if (keyUnit <= 0)
        {
            return false;
        }

        var minimumDistance = Math.Max(SwipeTapThreshold, keyUnit * SwipeActivationKeyDistance);
        return GetDistance(_pointerDownPosition, position) >= minimumDistance;
    }

    private void AddSwipePoint(Point position)
    {
        if (SwipeTrailLine.Points.Count == 0 ||
            GetDistance(SwipeTrailLine.Points[SwipeTrailLine.Points.Count - 1], position) >= 5)
        {
            SwipeTrailLine.Points.Add(position);
            _swipePoints.Add(position);
        }
    }

    private bool AddSwipeKey(KeySpec key)
    {
        if (key.Text is not { Length: 1 } text || !char.IsLetter(text[0]))
        {
            return false;
        }

        var letter = char.ToLowerInvariant(text[0]);
        if (_swipeLetters.Count == 0 || _swipeLetters[^1] != letter)
        {
            _swipeLetters.Add(letter);
            return true;
        }

        return false;
    }

    private bool AddSwipeKeyAt(Point position)
    {
        return TryGetNearestSwipeKey(position, out var key) && AddSwipeKey(key);
    }

    private void RefreshSwipeSuggestions()
    {
        var swipeSuggestions = GetCurrentSwipeSuggestions(_textSession.Context);
        if (swipeSuggestions.Count == 0)
        {
            return;
        }

        var suggestionsWereVisible = _suggestions.Count > 0;
        _suggestions.Clear();
        foreach (var suggestion in swipeSuggestions)
        {
            _suggestions.Add(suggestion);
        }

        ScheduleScaleUpdateIfSuggestionVisibilityChanged(suggestionsWereVisible);
    }

    private bool TryCommitSwipe()
    {
        if (PredictionsSuppressed || _control || _alt || _windows ||
            _swipeLetters.Distinct().Count() < MinimumActiveSwipeLetters)
        {
            return false;
        }

        var tracedLetters = new string([.. _swipeLetters]);
        var predictionContext = _textSession.Context;
        var candidateDiagnostics = GetSwipeCandidateDiagnostics(tracedLetters);
        var suggestion = GetCurrentSwipeSuggestions(predictionContext).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            RecordSwipeTrace(tracedLetters, null, null, committed: false, predictionContext, candidateDiagnostics);
            return false;
        }

        var prefix = _textSession.Context.CurrentWord.Length > 0 || _textSession.NeedsWordBoundaryBeforeNextWord
            ? " "
            : string.Empty;
        var output = $"{prefix}{GetSwipeOutputText(suggestion)}";
        try
        {
            KeyboardInput.SendText($"{output} ");
            LearnTypedText(_textSession.TypeText($"{output} "));
            RecordSwipeTrace(tracedLetters, suggestion, output, committed: true, predictionContext, candidateDiagnostics);
        }
        catch (InvalidOperationException ex)
        {
            ShowInputError(ex);
            RecordSwipeTrace(tracedLetters, suggestion, output, committed: false, predictionContext, candidateDiagnostics);
            return false;
        }

        ResetTransientModifiers();
        RefreshSuggestions();
        return true;
    }

    private IReadOnlyList<SwipeTraceCandidate> GetSwipeCandidateDiagnostics(string tracedLetters)
    {
        var keyUnit = GetAverageLetterKeySize();
        return _predictionEngine
            .GetSwipeCandidates(tracedLetters, _textSession.Context, 12)
            .Select((candidate, index) => new SwipeTraceCandidate(
                candidate,
                keyUnit <= 0 ? null : GetSwipeCandidateScore(candidate, keyUnit, index)))
            .ToArray();
    }

    private void RecordSwipeTrace(
        string tracedLetters,
        string? suggestion,
        string? outputText,
        bool committed,
        PredictionContext context,
        IReadOnlyList<SwipeTraceCandidate> candidates)
    {
        _swipeTraceRecorder.Append(new SwipeTraceEvent(
            DateTimeOffset.Now,
            tracedLetters,
            suggestion,
            outputText,
            committed,
            context.CurrentWord,
            context.PreviousWords,
            candidates));
    }

    private IReadOnlyList<string> GetCurrentSwipeSuggestions(PredictionContext context)
    {
        if (PredictionsSuppressed || _swipeLetters.Distinct().Count() < MinimumActiveSwipeLetters)
        {
            return [];
        }

        var tracedLetters = new string([.. _swipeLetters]);
        var geometrySuggestions = GetGeometryRankedSwipeSuggestions(tracedLetters, context);
        return geometrySuggestions.Count > 0
            ? geometrySuggestions
            : GetLanguageRankedSwipeSuggestions(tracedLetters, context);
    }

    private string? GetBestSwipeSuggestion()
    {
        return GetGeometryRankedSwipeSuggestions(new string([.. _swipeLetters]), _textSession.Context).FirstOrDefault();
    }

    private IReadOnlyList<string> GetLanguageRankedSwipeSuggestions(string tracedLetters, PredictionContext context)
    {
        return _predictionEngine.GetSwipeSuggestions(tracedLetters, context)
            .Take(4)
            .ToArray();
    }

    private IReadOnlyList<string> GetGeometryRankedSwipeSuggestions(string tracedLetters, PredictionContext context)
    {
        if (_swipePoints.Count < 2)
        {
            return [];
        }

        var keyUnit = GetAverageLetterKeySize();
        if (keyUnit <= 0)
        {
            return [];
        }

        if (TryGetNoisyShortSwipeOverride(tracedLetters, out var shortSuggestion))
        {
            return [shortSuggestion];
        }

        var candidates = _predictionEngine
            .GetSwipeCandidates(tracedLetters, context, SwipeCandidateLimit)
            .ToArray();

        if (TryGetShortLanguageSwipeOverride(candidates, out var languageSuggestion))
        {
            return candidates
                .Where(candidate => candidate != languageSuggestion)
                .Prepend(languageSuggestion)
                .Take(4)
                .ToArray();
        }

        var ranked = candidates
            .Select((candidate, index) => new
            {
                Candidate = candidate,
                Score = GetSwipeCandidateScore(candidate, keyUnit, index)
            })
            .Where(candidate => candidate.Score.HasValue)
            .OrderBy(candidate => candidate.Score!.Value)
            .ThenBy(candidate => candidate.Candidate.Length)
            .ToArray();

        if (ranked.Length == 0 || ranked[0].Score!.Value > SwipeMaximumGeometryScore)
        {
            return [];
        }

        if (ranked.Length > 1 &&
            ranked[0].Score!.Value > SwipeStrongGeometryScore &&
            ranked[1].Score!.Value - ranked[0].Score!.Value < SwipeMinimumScoreMargin)
        {
            var languageSuggestions = GetLanguageRankedSwipeSuggestions(tracedLetters, context);
            if (languageSuggestions.Count > 0 &&
                languageSuggestions[0].Equals(ranked[0].Candidate, StringComparison.OrdinalIgnoreCase))
            {
                return languageSuggestions;
            }
        }

        return ranked
            .Select(candidate => candidate.Candidate)
            .Take(4)
            .ToArray();
    }

    private static bool TryGetNoisyShortSwipeOverride(string tracedLetters, out string suggestion)
    {
        suggestion = string.Empty;
        var tracePattern = NormalizeSwipeLetters(tracedLetters);
        if (tracePattern.Length is < 3 or > 10)
        {
            return false;
        }

        foreach (var word in new[] { "is", "it", "if", "in", "ok", "on", "or", "we" })
        {
            if (tracePattern[0] == word[0] &&
                tracePattern[^1] == word[^1] &&
                IsOrderedSubsequence(word, tracePattern))
            {
                suggestion = word;
                return true;
            }
        }

        return false;
    }

    private bool TryGetShortLanguageSwipeOverride(IReadOnlyList<string> candidates, out string suggestion)
    {
        suggestion = string.Empty;

        if (candidates.Count == 0)
        {
            return false;
        }

        var tracePattern = new string([.. _swipeLetters]);
        var candidatePattern = NormalizeSwipeLetters(candidates[0]);
        if (tracePattern.Length is < 3 or > 5 || candidatePattern.Length is < 3 or > 6)
        {
            return false;
        }

        if (GetDamerauLevenshteinDistance(tracePattern, candidatePattern) > 1 ||
            Math.Abs(tracePattern.Length - candidatePattern.Length) > 1)
        {
            return false;
        }

        suggestion = candidates[0];
        return true;
    }

    private double? GetSwipeCandidateScore(string candidate, double keyUnit, int candidateRank)
    {
        var geometryScore = TryGetSwipeGeometryScore(candidate, keyUnit);
        if (!geometryScore.HasValue)
        {
            return null;
        }

        return geometryScore.Value +
               GetSwipeLetterMismatchPenalty(candidate) +
               (candidateRank * 0.018) -
               GetSwipeContextScoreBonus(candidate);
    }

    private double GetSwipeContextScoreBonus(string candidate)
    {
        var context = _textSession.Context;
        var previousWord = context.CurrentWord.Length > 0
            ? context.CurrentWord
            : context.PreviousWord;

        return _predictionEngine.GetContextualNextWordScore(previousWord, candidate) > 0
            ? SwipeContextScoreBonus
            : 0;
    }

    private double? TryGetSwipeGeometryScore(string candidate, double keyUnit)
    {
        var candidatePath = GetCandidateSwipePath(candidate);
        if (candidatePath.Count < 2)
        {
            return null;
        }

        const int sampleCount = 32;
        var strokeSamples = ResamplePolyline(_swipePoints, sampleCount);
        var candidateSamples = ResamplePolyline(candidatePath, sampleCount);
        if (strokeSamples.Count != sampleCount || candidateSamples.Count != sampleCount)
        {
            return null;
        }

        var total = 0.0;
        for (var index = 0; index < sampleCount; index++)
        {
            total += GetDistance(strokeSamples[index], candidateSamples[index]) / keyUnit;
        }

        var average = total / sampleCount;
        var endpointPenalty =
            (GetDistance(strokeSamples[0], candidateSamples[0]) +
             GetDistance(strokeSamples[^1], candidateSamples[^1])) /
            (keyUnit * 2);

        return average + (endpointPenalty * 0.45);
    }

    private double GetSwipeLetterMismatchPenalty(string candidate)
    {
        var tracePattern = new string([.. _swipeLetters]);
        var candidatePattern = NormalizeSwipeLetters(candidate);
        if (tracePattern.Length == 0 || candidatePattern.Length == 0)
        {
            return 0;
        }

        var editDistance = GetDamerauLevenshteinDistance(tracePattern, candidatePattern);
        var ignoredLetters = Math.Max(0, tracePattern.Length - candidatePattern.Length - 2);
        var lengthDifference = Math.Abs(tracePattern.Length - candidatePattern.Length);

        if (IsOrderedSubsequence(candidatePattern, tracePattern))
        {
            return Math.Min(0.18, Math.Max(0, tracePattern.Length - candidatePattern.Length) * 0.006);
        }

        if (IsOrderedSubsequence(tracePattern, candidatePattern))
        {
            return Math.Min(0.22, Math.Max(0, candidatePattern.Length - tracePattern.Length) * 0.035);
        }

        return (editDistance * 0.055) + (ignoredLetters * 0.11) + (lengthDifference * 0.04);
    }

    private static bool IsOrderedSubsequence(string expectedLetters, string tracedLetters)
    {
        if (expectedLetters.Length == 0)
        {
            return true;
        }

        var expectedIndex = 0;

        foreach (var letter in tracedLetters)
        {
            if (letter != expectedLetters[expectedIndex])
            {
                continue;
            }

            expectedIndex++;
            if (expectedIndex == expectedLetters.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSwipeLetters(string value)
    {
        var letters = new List<char>();
        char? previous = null;

        foreach (var character in value)
        {
            var letter = char.ToLowerInvariant(character);
            if (!char.IsLetter(letter) || letter == previous)
            {
                continue;
            }

            letters.Add(letter);
            previous = letter;
        }

        return new string([.. letters]);
    }

    private static int GetLevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static int GetDamerauLevenshteinDistance(string left, string right)
    {
        var previousPrevious = new int[right.Length + 1];
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                var distance = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);

                if (row > 1 &&
                    column > 1 &&
                    left[row - 1] == right[column - 2] &&
                    left[row - 2] == right[column - 1])
                {
                    distance = Math.Min(distance, previousPrevious[column - 2] + 1);
                }

                current[column] = distance;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[right.Length];
    }

    private IReadOnlyList<Point> GetCandidateSwipePath(string candidate)
    {
        var points = new List<Point>();
        char? previousLetter = null;

        foreach (var character in candidate)
        {
            var letter = char.ToLowerInvariant(character);
            if (!char.IsLetter(letter) || letter == previousLetter)
            {
                continue;
            }

            if (TryGetLetterKeyCenter(letter, out var center))
            {
                points.Add(center);
                previousLetter = letter;
            }
        }

        return points;
    }

    private bool TryGetLetterKeyCenter(char letter, out Point center)
    {
        foreach (var button in GetKeyButtons())
        {
            if (button.Tag is not KeySpec { Text.Length: 1 } key ||
                char.ToLowerInvariant(key.Text[0]) != letter)
            {
                continue;
            }

            center = button.TransformToAncestor(KeyboardGrid)
                .Transform(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            return true;
        }

        center = default;
        return false;
    }

    private bool TryGetNearestSwipeKey(Point position, out KeySpec key)
    {
        key = null!;
        var keyUnit = GetAverageLetterKeySize();
        if (keyUnit <= 0)
        {
            return false;
        }

        var maximumDistance = keyUnit * 1.05;
        var nearestDistance = double.MaxValue;

        foreach (var button in GetKeyButtons())
        {
            if (button.Tag is not KeySpec { Text.Length: 1 } candidate ||
                !char.IsLetter(candidate.Text[0]))
            {
                continue;
            }

            var center = button.TransformToAncestor(KeyboardGrid)
                .Transform(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            var distance = GetDistance(position, center);
            if (distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            key = candidate;
        }

        return nearestDistance <= maximumDistance;
    }

    private double GetAverageLetterKeySize()
    {
        var sizes = GetKeyButtons()
            .Where(button => button.Tag is KeySpec { Text.Length: 1 } key && char.IsLetter(key.Text[0]))
            .Select(button => Math.Min(button.ActualWidth, button.ActualHeight))
            .Where(size => size > 0)
            .Order()
            .ToArray();

        return sizes.Length == 0 ? 0 : sizes[sizes.Length / 2];
    }

    private IEnumerable<Button> GetKeyButtons()
    {
        return KeyboardGrid.Children
            .OfType<Grid>()
            .SelectMany(row => row.Children.OfType<Button>());
    }

    private static IReadOnlyList<Point> ResamplePolyline(IReadOnlyList<Point> points, int sampleCount)
    {
        if (points.Count == 0 || sampleCount <= 0)
        {
            return [];
        }

        if (points.Count == 1 || sampleCount == 1)
        {
            return Enumerable.Repeat(points[0], sampleCount).ToArray();
        }

        var distances = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
        {
            distances[index] = distances[index - 1] + GetDistance(points[index - 1], points[index]);
        }

        var totalLength = distances[^1];
        if (totalLength <= 0)
        {
            return Enumerable.Repeat(points[0], sampleCount).ToArray();
        }

        var samples = new Point[sampleCount];
        var segmentIndex = 1;

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var targetDistance = totalLength * sampleIndex / (sampleCount - 1);
            while (segmentIndex < distances.Length - 1 && distances[segmentIndex] < targetDistance)
            {
                segmentIndex++;
            }

            var segmentStart = distances[segmentIndex - 1];
            var segmentLength = distances[segmentIndex] - segmentStart;
            var ratio = segmentLength <= 0 ? 0 : (targetDistance - segmentStart) / segmentLength;
            var start = points[segmentIndex - 1];
            var end = points[segmentIndex];
            samples[sampleIndex] = new Point(
                start.X + ((end.X - start.X) * ratio),
                start.Y + ((end.Y - start.Y) * ratio));
        }

        return samples;
    }

    private string GetSwipeOutputText(string suggestion)
    {
        if (suggestion.Length == 0)
        {
            return suggestion;
        }

        if (_capsLock && !_shift)
        {
            return suggestion.ToUpperInvariant();
        }

        if (!_shift)
        {
            return suggestion;
        }

        return suggestion.Length == 1
            ? suggestion.ToUpperInvariant()
            : $"{char.ToUpperInvariant(suggestion[0])}{suggestion[1..]}";
    }

    private void ResetPointerGesture()
    {
        _pointerGestureActive = false;
        _swipeGestureActive = false;
        _pointerDownKey = null;
        _swipeLetters.Clear();
        _swipePoints.Clear();
        SwipeTrailLine.Opacity = 0;
        SwipeTrailLine.Points.Clear();
    }

    private KeySpec? GetKeyAt(Point position)
    {
        var hit = VisualTreeHelper.HitTest(KeyboardGrid, position);
        var current = hit?.VisualHit as DependencyObject;

        while (current is not null)
        {
            if (current is Button { Tag: KeySpec key })
            {
                return key;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static double GetDistance(Point left, Point right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private void ToggleModifier(ref bool modifier)
    {
        modifier = !modifier;
        RefreshKeyLabels();
    }

    private void SendTextKey(KeySpec key)
    {
        var text = GetOutputText(key);
        var shortcutModifiersActive = _control || _alt || _windows;

        if (shortcutModifiersActive && key.VirtualKey.HasValue)
        {
            KeyboardInput.SendVirtualKeyChord(GetActiveModifiers(), key.VirtualKey.Value);
            LearnTypedText(_textSession.CommitBoundary());
            return;
        }

        KeyboardInput.SendText(text);
        if (PredictionsSuppressed)
        {
            return;
        }

        TrackInputText(text);
        LearnTypedText(_textSession.TypeText(text));
    }

    private void LearnTypedText(TextCommit? commit)
    {
        if (commit is not null)
        {
            LearnTypedText([commit]);
        }
    }

    private void LearnTypedText(IEnumerable<TextCommit> commits)
    {
        if (!CanLearn)
        {
            return;
        }

        _predictionEngine.LearnTypedText(commits);
    }

    private void ScheduleFocusedInputResync(bool allowEmptyContext)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (PredictionsSuppressed)
            {
                return;
            }

            RequestFocusedInputSeed(allowEmptyContext);
        });
    }

    private void TrackInputText(string text)
    {
        foreach (var character in text)
        {
            if (character is '\r' or '\n' or '\t')
            {
                TrackSubmittedInputLine();
                continue;
            }

            _inputLineBuffer.Append(character);
            if (_inputLineBuffer.Length > 500)
            {
                _inputLineBuffer.Remove(0, _inputLineBuffer.Length - 500);
            }
        }
    }

    private void TrackInputBackspace()
    {
        if (_inputLineBuffer.Length > 0)
        {
            _inputLineBuffer.Remove(_inputLineBuffer.Length - 1, 1);
        }
    }

    private void TrackSubmittedInputLine()
    {
        var line = _inputLineBuffer.ToString();
        _inputLineBuffer.Clear();

        if (!SensitiveInputMonitor.IsCredentialPromptCommand(line))
        {
            return;
        }

        _focusedTextContextSensitive = true;
        _textSession.ResetPredictionContext();
        RefreshPrivacyState();
        RefreshSuggestions();
    }

    private void ClearSensitiveTextContextAfterSubmission()
    {
        if (!_focusedTextContextSensitive)
        {
            return;
        }

        _focusedTextContextSensitive = false;
        _inputLineBuffer.Clear();
        _textSession.ResetPredictionContext();
        RefreshPrivacyState();
        RequestFocusedInputSeed(allowEmptyContext: true);
    }

    internal void ApplyPhysicalKeyToPredictionSession(PhysicalKeyPressedEventArgs key)
    {
        var virtualKey = (VirtualKey)key.VirtualKey;

        if (PredictionsSuppressed)
        {
            if (virtualKey is VirtualKey.Enter or VirtualKey.Tab)
            {
                TrackSubmittedInputLine();
                ClearSensitiveTextContextAfterSubmission();
            }

            return;
        }

        if (IsNavigationKey(virtualKey))
        {
            if (key.Shift)
            {
                _physicalSelectionActive = true;
            }
            else
            {
                _physicalSelectionActive = false;
                _textSession.ResetPredictionContext();
                RequestFocusedInputSeed(allowEmptyContext: false);
            }

            RefreshSuggestions();
            return;
        }

        switch (virtualKey)
        {
            case VirtualKey.Back:
                TrackInputBackspace();
                if (_physicalSelectionActive)
                {
                    ResetAfterPhysicalSelectionEdit();
                }
                else if (key.Control)
                {
                    _textSession.BackspaceWord();
                }
                else
                {
                    _textSession.Backspace();
                }

                break;
            case VirtualKey.Delete:
                if (_physicalSelectionActive)
                {
                    ResetAfterPhysicalSelectionEdit();
                }

                break;
            case VirtualKey.Tab:
            case VirtualKey.Enter:
                _physicalSelectionActive = false;
                TrackSubmittedInputLine();
                LearnTypedText(_textSession.CommitBoundary());
                break;
            default:
                if (key.Text.Length == 0)
                {
                    return;
                }

                if (_physicalSelectionActive)
                {
                    _textSession.ResetPredictionContext();
                    _physicalSelectionActive = false;
                }

                LearnTypedText(_textSession.TypeText(key.Text));
                TrackInputText(key.Text);
                break;
        }

        RefreshSuggestions();
    }

    private bool ShouldProcessPhysicalKeyboardEvent(PhysicalKeyPressedEventArgs key)
    {
        if (_focusedTextInputActive)
        {
            return true;
        }

        var virtualKey = (VirtualKey)key.VirtualKey;
        return _focusedTextContextSensitive && virtualKey is VirtualKey.Enter or VirtualKey.Tab;
    }

    private void ResetAfterPhysicalSelectionEdit()
    {
        _physicalSelectionActive = false;
        _textSession.ResetPredictionContext();
        RequestFocusedInputSeed(allowEmptyContext: false);
    }

    private static bool IsNavigationKey(VirtualKey key)
    {
        return key is
            VirtualKey.Left or
            VirtualKey.Right or
            VirtualKey.Up or
            VirtualKey.Down or
            VirtualKey.Home or
            VirtualKey.End or
            VirtualKey.PageUp or
            VirtualKey.PageDown;
    }

    private void SendVirtualKey(KeySpec key)
    {
        if (!key.VirtualKey.HasValue)
        {
            return;
        }

        var modifiers = GetActiveModifiers();
        if (modifiers.Count > 0)
        {
            KeyboardInput.SendVirtualKeyChord(modifiers, key.VirtualKey.Value);
            return;
        }

        KeyboardInput.SendVirtualKey(key.VirtualKey.Value);
    }

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (PredictionsSuppressed || sender is not Button { Content: string suggestion } || string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        AcceptSuggestion(suggestion, releasePhysicalModifiers: false);
    }

    private void RemoveSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: Button { Content: string suggestion } } } ||
            string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        _predictionEngine.RemoveSuggestion(suggestion);
        _suggestions.Remove(suggestion);
        FooterHintText = $"Removed \"{suggestion}\" from suggestions.";
        RefreshSuggestions();
    }

    private void AcceptFirstSuggestionFromShortcut()
    {
        if (PredictionsSuppressed || _suggestions.FirstOrDefault() is not { Length: > 0 } suggestion)
        {
            return;
        }

        AcceptSuggestion(suggestion, releasePhysicalModifiers: true);
    }

    private void AcceptSuggestion(string suggestion, bool releasePhysicalModifiers)
    {
        var previousWord = _textSession.Context.PreviousWord;

        try
        {
            var replacement = _textSession.AcceptSuggestion(suggestion);
            void SendReplacement()
            {
                for (var i = 0; i < replacement.BackspaceCount; i++)
                {
                    KeyboardInput.SendVirtualKey(VirtualKey.Back);
                }

                KeyboardInput.SendText(replacement.Text);
            }

            if (releasePhysicalModifiers)
            {
                KeyboardInput.SendWithReleasedModifiers(SendReplacement);
            }
            else
            {
                SendReplacement();
            }
        }
        catch (InvalidOperationException ex)
        {
            ShowInputError(ex);
            return;
        }

        if (CanLearn)
        {
            _predictionEngine.LearnAcceptedSuggestion(suggestion, previousWord);
        }

        RefreshSuggestions();
    }

    private void Prediction_Changed(object sender, RoutedEventArgs e)
    {
        _predictionsEnabled = sender is CheckBox { IsChecked: true };
        if (PrivacyStatus is null)
        {
            return;
        }

        RefreshPrivacyState();
        RefreshSuggestions();

        if (_predictionsEnabled && _startFocusMonitors)
        {
            _sensitiveInputMonitor.RequestRefresh();
            RefreshFocusedInputState();
        }
    }

    private void Learning_Changed(object sender, RoutedEventArgs e)
    {
        _learningEnabled = sender is CheckBox { IsChecked: true };
        if (PrivacyStatus is null)
        {
            return;
        }

        RefreshPrivacyState();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updateAvailable)
        {
            await CheckForUpdatesAsync();
            return;
        }

        await CheckForUpdatesNowAsync();
    }

    private async void CheckForUpdatesNow_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesNowAsync();
    }

    private void TypingTest_Click(object sender, RoutedEventArgs e)
    {
        if (_typingTestWindow is { IsVisible: true })
        {
            _typingTestWindow.Activate();
            return;
        }

        _typingTestWindow = new TypingTestWindow
        {
            Owner = this
        };
        _typingTestWindow.Closed += (_, _) => _typingTestWindow = null;
        _typingTestWindow.Show();
    }

    private async Task CheckForUpdatesAsync()
    {
        _updateOperationInProgress = true;
        UpdateButton.IsEnabled = false;
        var previousHint = FooterHintText;

        try
        {
            FooterHintText = "Checking GitHub for updates...";
            var update = await _releaseUpdater.CheckForUpdateAsync();
            ApplyUpdateAvailability(update);
            FooterHintText = update.Message;

            if (!update.IsUpdateAvailable)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(update.InstallerUrl))
            {
                if (!string.IsNullOrWhiteSpace(update.ReleaseUrl))
                {
                    OpenUrl(update.ReleaseUrl);
                }

                FooterHintText = "Update found, but no MSI installer was attached.";
                return;
            }

            FooterHintText = $"Downloading Keebs {update.LatestVersion}...";
            var installerPath = await _releaseUpdater.DownloadInstallerAsync(update);

            FooterHintText = "Launching installer...";
            GitHubReleaseUpdater.LaunchInstallerAndRestart(installerPath);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            FooterHintText = $"Update check failed: {ex.Message}";
        }
        finally
        {
            _updateOperationInProgress = false;
            if (IsLoaded)
            {
                UpdateButton.IsEnabled = true;
            }

            if (FooterHintText.Length == 0)
            {
                FooterHintText = previousHint;
            }
        }
    }

    private async Task CheckForUpdatesNowAsync()
    {
        if (_updateOperationInProgress)
        {
            return;
        }

        _updateOperationInProgress = true;
        UpdateButton.IsEnabled = false;
        var previousHint = FooterHintText;

        try
        {
            FooterHintText = "Checking GitHub for updates...";
            var update = await _releaseUpdater.CheckForUpdateAsync();
            ApplyUpdateAvailability(update);
            FooterHintText = update.Message;
        }
        catch (Exception ex)
        {
            FooterHintText = $"Update check failed: {ex.Message}";
        }
        finally
        {
            _updateOperationInProgress = false;
            if (IsLoaded)
            {
                UpdateButton.IsEnabled = true;
            }

            if (FooterHintText.Length == 0)
            {
                FooterHintText = previousHint;
            }
        }
    }

    private void StartAutomaticUpdateChecks()
    {
        _updateCheckTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher)
        {
            Interval = AutomaticUpdateCheckInterval
        };
        _updateCheckTimer.Tick += async (_, _) => await RefreshUpdateAvailabilityAsync();
        _updateCheckTimer.Start();
        _ = RefreshUpdateAvailabilityAsync();
    }

    private async Task RefreshUpdateAvailabilityAsync()
    {
        if (Interlocked.CompareExchange(ref _updateCheckInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var update = await _releaseUpdater.CheckForUpdateAsync();
            if (IsLoaded)
            {
                ApplyUpdateAvailability(update);
            }
        }
        catch (Exception)
        {
            if (IsLoaded)
            {
                UpdateButton.ToolTip = "Could not check automatically. Click to retry.";
            }
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInFlight, 0);
        }
    }

    internal void ApplyUpdateAvailability(UpdateCheckResult update)
    {
        _updateAvailable = update.IsUpdateAvailable;
        UpdateButton.Tag = _updateAvailable ? "Available" : null;
        UpdateButton.IsEnabled = !_updateOperationInProgress;
        UpdateButton.ToolTip = update.IsUpdateAvailable
            ? $"Install Keebs {update.LatestVersion}"
            : $"Check for updates (current: {update.CurrentVersion})";
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private bool PredictionsSuppressed =>
        !_predictionsEnabled ||
        _startFocusMonitors && !_focusedTextInputActive ||
        _sensitiveInputMonitor.IsSensitive ||
        _focusedTextContextSensitive;

    private bool CanLearn => _learningEnabled && !PredictionsSuppressed;

    private void RefreshPrivacyState()
    {
        if (!_predictionsEnabled)
        {
            PrivacyStatus.Text = "Predictions off";
            FooterHintText = "Predictions and learning are paused.";
            StatusAccentBrush = new SolidColorBrush(Color.FromRgb(227, 178, 87));
            return;
        }

        if (_sensitiveInputMonitor.IsSensitive || _focusedTextContextSensitive)
        {
            PrivacyStatus.Text = "Sensitive field: raw keys";
            FooterHintText = "Sensitive fields get raw key input only.";
            StatusAccentBrush = new SolidColorBrush(Color.FromRgb(235, 111, 111));
            return;
        }

        if (!_learningEnabled)
        {
            PrivacyStatus.Text = "Predictions on";
            FooterHintText = "Learning is paused. Suggestions stay local.";
            StatusAccentBrush = new SolidColorBrush(Color.FromRgb(109, 168, 196));
            return;
        }

        PrivacyStatus.Text = "Predictions on";
        FooterHintText = "Predictions and learning are local.";
        StatusAccentBrush = new SolidColorBrush(Color.FromRgb(109, 196, 137));
    }

    private void RefreshSuggestions()
    {
        var suggestionsWereVisible = _suggestions.Count > 0;
        _suggestions.Clear();

        if (PredictionsSuppressed)
        {
            _textSession.ResetPredictionContext();
            ScheduleScaleUpdateIfSuggestionVisibilityChanged(suggestionsWereVisible);
            return;
        }

        foreach (var suggestion in _predictionEngine.GetSuggestions(_textSession.Context).Take(4))
        {
            _suggestions.Add(suggestion);
        }

        ScheduleScaleUpdateIfSuggestionVisibilityChanged(suggestionsWereVisible);
    }

    private void ScheduleScaleUpdateIfSuggestionVisibilityChanged(bool suggestionsWereVisible)
    {
        if (suggestionsWereVisible != (_suggestions.Count > 0))
        {
            ScheduleScaleUpdate();
        }
    }

    private void RefreshFocusedInputState()
    {
        _focusedTextInputActive = FocusedTextContextReader.IsFocusedElementTextInput();
        _focusedTextContextSensitive = false;
        _inputLineBuffer.Clear();
        RefreshPrivacyState();

        if (PredictionsSuppressed)
        {
            RefreshSuggestions();
            return;
        }

        _textSession.ResetPredictionContext();
        RefreshSuggestions();
        RequestFocusedInputSeed(allowEmptyContext: true);
    }

    private void RequestFocusedInputSeed(bool allowEmptyContext)
    {
        _focusedContextAllowEmptyContext = allowEmptyContext;
        Interlocked.Increment(ref _focusedContextRequestId);
        TryStartFocusedInputSeedRead();
    }

    private void TryStartFocusedInputSeedRead()
    {
        if (Interlocked.CompareExchange(ref _focusedContextReadInFlight, 1, 0) != 0)
        {
            return;
        }

        var requestId = Volatile.Read(ref _focusedContextRequestId);
        _ = ReadFocusedInputContextAsync(requestId, _focusedContextAllowEmptyContext);
    }

    private void CompleteFocusedInputSeedRead(int requestId)
    {
        Interlocked.Exchange(ref _focusedContextReadInFlight, 0);

        if (requestId != Volatile.Read(ref _focusedContextRequestId))
        {
            TryStartFocusedInputSeedRead();
        }
    }

    private async Task ReadFocusedInputContextAsync(int requestId, bool allowEmptyContext)
    {
        string textBeforeCaret;

        try
        {
            var readTask = Task.Run(FocusedTextContextReader.GetTextBeforeCaret);
            var completedTask = await Task.WhenAny(readTask, Task.Delay(FocusedContextReadTimeout)).ConfigureAwait(false);
            if (completedTask != readTask)
            {
                _ = readTask.ContinueWith(
                    completedRead =>
                    {
                        _ = completedRead.Exception;
                        CompleteFocusedInputSeedRead(requestId);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return;
            }

            textBeforeCaret = await readTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            CompleteFocusedInputSeedRead(requestId);
            return;
        }

        CompleteFocusedInputSeedRead(requestId);

        await Dispatcher.InvokeAsync(() =>
        {
            if (requestId != Volatile.Read(ref _focusedContextRequestId) || !_predictionsEnabled || _sensitiveInputMonitor.IsSensitive)
            {
                return;
            }

            _focusedTextContextSensitive = SensitiveInputMonitor.IsSensitiveTextContext(textBeforeCaret);
            if (_focusedTextContextSensitive)
            {
                _textSession.ResetPredictionContext();
                RefreshPrivacyState();
                RefreshSuggestions();
                return;
            }

            SeedTextSessionFromFocusedInput(textBeforeCaret, allowEmptyContext);
            RefreshSuggestions();
        });
    }

    private void SeedTextSessionFromFocusedInput(string textBeforeCaret, bool allowEmptyContext)
    {
        if (allowEmptyContext || textBeforeCaret.Length > 0)
        {
            _textSession.SeedFromTextBeforeCaret(textBeforeCaret);
        }
    }

    private IReadOnlyList<VirtualKey> GetActiveModifiers()
    {
        var modifiers = new List<VirtualKey>(4);

        if (_control)
        {
            modifiers.Add(VirtualKey.Control);
        }

        if (_shift)
        {
            modifiers.Add(VirtualKey.Shift);
        }

        if (_alt)
        {
            modifiers.Add(VirtualKey.Alt);
        }

        if (_windows)
        {
            modifiers.Add(VirtualKey.LeftWindows);
        }

        return modifiers;
    }

    private void ResetTransientModifiers()
    {
        _shift = false;
        _control = false;
        _alt = false;
        _windows = false;
        RefreshKeyLabels();
    }

    private string GetOutputText(KeySpec key)
    {
        var text = _shift && key.ShiftedText is not null ? key.ShiftedText : key.Text ?? string.Empty;

        if (text.Length == 1 && char.IsLetter(text[0]))
        {
            return _shift ^ _capsLock
                ? text.ToUpperInvariant()
                : text.ToLowerInvariant();
        }

        return text;
    }

    private void RefreshKeyLabels()
    {
        foreach (var button in KeyboardGrid.Children
                     .OfType<Grid>()
                     .SelectMany(row => row.Children.OfType<Button>()))
        {
            if (button.Tag is not KeySpec key)
            {
                continue;
            }

            button.Content = GetKeyDisplay(key);
            button.FontFamily = GetKeyFontFamily(key);

            if (IsActiveModifier(key))
            {
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(227, 178, 87));
            }
            else
            {
                button.ClearValue(BorderBrushProperty);
            }
        }
    }

    private bool IsActiveModifier(KeySpec key)
    {
        return key.Id switch
        {
            "ShiftLeft" or "ShiftRight" => _shift,
            "CapsLock" => _capsLock,
            "CtrlLeft" or "CtrlRight" => _control,
            "AltLeft" or "AltRight" => _alt,
            "WinLeft" or "WinRight" => _windows,
            _ => false
        };
    }

    private void ShowInputError(Exception exception)
    {
        PrivacyStatus.Text = "Input failed";
        PrivacyStatus.ToolTip = exception.Message;
        StatusAccentBrush = new SolidColorBrush(Color.FromRgb(235, 111, 111));
    }

    private void UpdateScale()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var compactness = Math.Min(width / 1220, height / 440);
        var keyHeightScale = GetKeyHeightScale(height);
        var compactHeight = height < 330;

        StatusFontSize = Math.Clamp(12.5 * compactness, 9.5, 12.5);
        StatusDotSize = Math.Clamp(7 * compactness, 4.5, 7);
        var margin = Math.Clamp(10 * compactness, 2, 10);
        OuterMargin = new Thickness(margin);
        var shellPadding = Math.Clamp(8 * compactness, 3, 8);
        ShellPadding = new Thickness(shellPadding);
        FooterBar.Visibility = Visibility.Visible;
        HeaderBar.Visibility = Visibility.Visible;
        HeaderBar.Margin = new Thickness(0, 0, 0, compactHeight ? 1 : 6);
        FooterBar.Margin = new Thickness(0, compactHeight ? 3 : 7, 0, 0);

        var keyMinHeight = Math.Clamp(44 * keyHeightScale, compactHeight ? 0 : 24, 44);
        var verticalKeyMargin = Math.Clamp(2.2 * keyHeightScale, compactHeight ? 0.45 : 1, 2.2);
        var horizontalDeckPadding = Math.Clamp(5 * compactness, 1, 5);
        var verticalDeckPadding = Math.Clamp(5 * keyHeightScale, 1, 5);
        (keyMinHeight, verticalKeyMargin, verticalDeckPadding) = FitKeyboardDeckMetrics(
            keyMinHeight,
            verticalKeyMargin,
            verticalDeckPadding,
            GetAvailableKeyboardDeckHeight());

        KeyFontSize = Math.Clamp(keyMinHeight * 0.68, 13.5, 16);
        DeckPadding = new Thickness(horizontalDeckPadding, verticalDeckPadding, horizontalDeckPadding, verticalDeckPadding);
        KeyboardDeck.Height = GetKeyboardDeckHeight(keyMinHeight, verticalKeyMargin, verticalDeckPadding);
        KeyboardDeck.MaxHeight = KeyboardDeck.Height;

        foreach (var row in KeyboardGrid.Children.OfType<Grid>())
        {
            row.Margin = GetRowMargin(Grid.GetRow(row));

            foreach (var button in row.Children.OfType<Button>())
            {
                var horizontalKeyMargin = Math.Clamp(2.2 * compactness, compactHeight ? 0.45 : 1, 2.2);
                button.Margin = new Thickness(horizontalKeyMargin, verticalKeyMargin, horizontalKeyMargin, verticalKeyMargin);
                button.MinHeight = keyMinHeight;

                if (button.Tag is KeySpec key)
                {
                    button.Content = GetKeyDisplay(key);
                    button.FontFamily = GetKeyFontFamily(key);
                    var compactUtilityLabel = width < 700 && IsCompactUtilityLabel(key);
                    button.Padding = compactUtilityLabel ? new Thickness(0) : new Thickness(2, 0, 2, 0);
                    button.FontSize = compactUtilityLabel
                        ? Math.Min(KeyFontSize, 9.5)
                        : IsIconKey(key)
                            ? Math.Min(KeyFontSize * 0.9, keyMinHeight * 0.68)
                            : KeyFontSize;
                }
            }
        }

        FooterHint.Visibility = width < 900 || height < 350
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ScheduleScaleUpdate()
    {
        Dispatcher.BeginInvoke(UpdateScale, DispatcherPriority.Loaded);
    }

    private static double GetKeyHeightScale(double height)
    {
        const double compactHeight = 245;
        const double fullHeight = 440;
        const double compactScale = 600.0 / 1220.0;

        var progress = Math.Clamp((height - compactHeight) / (fullHeight - compactHeight), 0, 1);
        return compactScale + ((1 - compactScale) * progress);
    }

    private double GetKeyboardDeckHeight(double keyMinHeight, double verticalKeyMargin, double deckPadding)
    {
        var rowCount = Math.Max(1, KeyboardGrid.RowDefinitions.Count);
        return (rowCount * (keyMinHeight + (verticalKeyMargin * 2))) + (deckPadding * 2) + 2;
    }

    private double GetAvailableKeyboardDeckHeight()
    {
        if (RootGrid.ActualHeight <= 0)
        {
            return double.PositiveInfinity;
        }

        return Math.Max(
            1,
            RootGrid.ActualHeight -
            HeaderBar.ActualHeight -
            HeaderBar.Margin.Top -
            HeaderBar.Margin.Bottom -
            FooterBar.ActualHeight -
            FooterBar.Margin.Top -
            FooterBar.Margin.Bottom);
    }

    private (double KeyMinHeight, double VerticalKeyMargin, double DeckPadding) FitKeyboardDeckMetrics(
        double keyMinHeight,
        double verticalKeyMargin,
        double deckPadding,
        double availableHeight)
    {
        if (double.IsInfinity(availableHeight) ||
            GetKeyboardDeckHeight(keyMinHeight, verticalKeyMargin, deckPadding) <= availableHeight)
        {
            return (keyMinHeight, verticalKeyMargin, deckPadding);
        }

        var rowCount = Math.Max(1, KeyboardGrid.RowDefinitions.Count);
        var fittedDeckPadding = Math.Min(deckPadding, 1);
        var fittedVerticalMargin = Math.Min(verticalKeyMargin, 0.45);
        var fittedKeyHeight = (availableHeight - (fittedDeckPadding * 2) - 2) / rowCount -
                              (fittedVerticalMargin * 2);

        return (Math.Max(12, fittedKeyHeight), fittedVerticalMargin, fittedDeckPadding);
    }

    private Thickness GetRowMargin(int rowIndex)
    {
        return new Thickness(0);
    }

    private string GetKeyDisplay(KeySpec key)
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var activeDisplay = GetActiveKeyDisplay(key);

        if (TryGetIconDisplay(key, out var iconDisplay))
        {
            return iconDisplay;
        }

        if (width < 900)
        {
            return key.Id switch
            {
                "Backspace" => "Bksp",
                "Delete" => "Del",
                "Insert" => "Ins",
                "Home" => "Hm",
                "PageUp" => width < 700 ? "Pg↑" : "PgUp",
                "PageDown" => width < 700 ? "Pg↓" : "PgDn",
                "PrintScreen" => "Prt",
                "ScrollLock" => "Scr",
                "Pause" => "Pau",
                "Left" => "<",
                "Right" => ">",
                "Up" => "^",
                "Down" => "v",
                _ => activeDisplay.Replace("\n", string.Empty, StringComparison.Ordinal)
            };
        }

        return activeDisplay;
    }

    private string GetActiveKeyDisplay(KeySpec key)
    {
        if (key.Text is null)
        {
            return key.Display;
        }

        if (key.Text.Length == 1 && char.IsLetter(key.Text[0]))
        {
            return _shift ^ _capsLock
                ? key.Text.ToUpperInvariant()
                : key.Text.ToLowerInvariant();
        }

        if (_shift && key.ShiftedText is not null)
        {
            return key.ShiftedText;
        }

        return key.Text == " " ? key.Display : key.Text;
    }

    private static bool TryGetIconDisplay(KeySpec key, out string iconDisplay)
    {
        iconDisplay = key.Id switch
        {
            "Backspace" => "⌫",
            "Tab" => "⇥",
            "CapsLock" => "⇪",
            "ShiftLeft" or "ShiftRight" => "⇧",
            "Enter" => "↵",
            "WinLeft" or "WinRight" => "⊞",
            "Menu" => "☰",
            "Left" => "←",
            "Right" => "→",
            "Up" => "↑",
            "Down" => "↓",
            _ => string.Empty
        };

        return iconDisplay.Length > 0;
    }

    private static bool IsIconKey(KeySpec key)
    {
        return TryGetIconDisplay(key, out _);
    }

    private static bool IsAccentKey(KeySpec key)
    {
        return key.Id is
            "Escape" or "Enter" or "Space" or
            "Left" or "Up" or "Down" or "Right";
    }

    private static bool IsCompactUtilityLabel(KeySpec key)
    {
        return key.Id is
            "PrintScreen" or "ScrollLock" or "Pause" or
            "Insert" or "Home" or "PageUp" or
            "Delete" or "End" or "PageDown";
    }

    private static FontFamily GetKeyFontFamily(KeySpec key)
    {
        return IsIconKey(key) ? IconKeyFontFamily : TextKeyFontFamily;
    }

    private static KeySpec Key(
        string id,
        string display,
        double width = 1,
        bool utility = false,
        string? text = null,
        string? shiftedText = null,
        VirtualKey? virtualKey = null)
    {
        return new KeySpec(id, display, width, utility, text, shiftedText, virtualKey);
    }

    private static KeySlot Slot(KeySpec key)
    {
        return new KeySlot(key.Width, key);
    }

    private static KeySlot Gap(double width)
    {
        return new KeySlot(width, null);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static class NativeMethods
    {
        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HitTestCaption = 2;

        public static void MakeNoActivate(IntPtr hwnd)
        {
            var style = GetWindowLong(hwnd, GwlExStyle);
            _ = SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate);
        }

        public static void BeginWindowDrag(IntPtr hwnd)
        {
            ReleaseCapture();
            _ = SendMessage(hwnd, WmNcLeftButtonDown, new IntPtr(HitTestCaption), IntPtr.Zero);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }

    private sealed record KeySpec(
        string Id,
        string Display,
        double Width,
        bool IsUtility,
        string? Text,
        string? ShiftedText,
        VirtualKey? VirtualKey);

    private sealed record KeySlot(double Width, KeySpec? Key);
}
