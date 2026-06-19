using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Keebs;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly TimeSpan FocusedContextReadTimeout = TimeSpan.FromMilliseconds(300);
    private readonly ObservableCollection<string> _suggestions = [];
    private readonly TextPredictionEngine _predictionEngine;
    private readonly TextSession _textSession = new();
    private readonly SensitiveInputMonitor _sensitiveInputMonitor = new();
    private readonly PhysicalKeyboardMonitor _physicalKeyboardMonitor = new();
    private static readonly FontFamily TextKeyFontFamily = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily IconKeyFontFamily = new("Segoe UI Symbol, Segoe UI");
    private bool _shift;
    private bool _capsLock;
    private bool _control;
    private bool _alt;
    private bool _windows;
    private bool _predictionsEnabled = true;
    private bool _learningEnabled = true;
    private bool _physicalSelectionActive;
    private int _focusedContextReadInFlight;
    private int _focusedContextRequestId;
    private double _keyFontSize = 14;
    private double _statusFontSize = 12;
    private double _statusDotSize = 7;
    private Thickness _outerMargin = new(12);
    private Thickness _shellPadding = new(10);
    private Thickness _deckPadding = new(7);
    private Brush _statusAccentBrush = new SolidColorBrush(Color.FromRgb(109, 196, 137));
    private string _footerHintText = "Predictions are local. Sensitive fields get raw key input only.";

    public MainWindow()
        : this(new TextPredictionEngine())
    {
    }

    internal MainWindow(TextPredictionEngine predictionEngine)
    {
        _predictionEngine = predictionEngine;
        InitializeComponent();
        DataContext = this;
        SuggestionStrip.ItemsSource = _suggestions;
        BuildKeyboard();

        _sensitiveInputMonitor.FocusChanged += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                RefreshFocusedInputState();
            });
        _physicalKeyboardMonitor.TextInputKeyPressed += (_, key) =>
        {
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
            _sensitiveInputMonitor.Start();
            _physicalKeyboardMonitor.Start();
            RefreshFocusedInputState();
            UpdateScale();
        };
        Closed += (_, _) =>
        {
            _physicalKeyboardMonitor.Dispose();
            _sensitiveInputMonitor.Dispose();
        };
        SizeChanged += (_, _) => UpdateScale();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

            var button = new Button
            {
                Content = GetKeyDisplay(slot.Key),
                Tag = slot.Key,
                FontFamily = GetKeyFontFamily(slot.Key),
                Style = (Style)FindResource(slot.Key.IsUtility
                    ? "UtilityButton"
                    : "KeyboardButton")
            };

            button.Click += Key_Click;
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
                    _textSession.Backspace();
                    ScheduleFocusedInputResync(allowEmptyContext: false);
                    break;
                case "Tab":
                    SendVirtualKey(key);
                    LearnTypedText(_textSession.CommitBoundary());
                    break;
                case "Enter":
                    SendVirtualKey(key);
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

    internal void ApplyPhysicalKeyToPredictionSession(PhysicalKeyPressedEventArgs key)
    {
        if (PredictionsSuppressed)
        {
            return;
        }

        var virtualKey = (VirtualKey)key.VirtualKey;

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
                break;
        }

        RefreshSuggestions();
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

    private bool PredictionsSuppressed => !_predictionsEnabled || _sensitiveInputMonitor.IsSensitive;

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

        if (_sensitiveInputMonitor.IsSensitive)
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
        _suggestions.Clear();

        if (PredictionsSuppressed)
        {
            _textSession.ResetPredictionContext();
            return;
        }

        foreach (var suggestion in _predictionEngine.GetSuggestions(_textSession.Context).Take(4))
        {
            _suggestions.Add(suggestion);
        }
    }

    private void RefreshFocusedInputState()
    {
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
        var requestId = Interlocked.Increment(ref _focusedContextRequestId);
        if (Interlocked.CompareExchange(ref _focusedContextReadInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = ReadFocusedInputContextAsync(requestId, allowEmptyContext);
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
                    _ => Interlocked.Exchange(ref _focusedContextReadInFlight, 0),
                    TaskScheduler.Default);
                return;
            }

            textBeforeCaret = await readTask.ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _focusedContextReadInFlight, 0);
            return;
        }
        catch (System.Windows.Automation.ElementNotAvailableException)
        {
            Interlocked.Exchange(ref _focusedContextReadInFlight, 0);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Interlocked.Exchange(ref _focusedContextReadInFlight, 0);
            return;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _focusedContextReadInFlight, 0);
            return;
        }

        Interlocked.Exchange(ref _focusedContextReadInFlight, 0);

        await Dispatcher.InvokeAsync(() =>
        {
            if (requestId != Volatile.Read(ref _focusedContextRequestId) || PredictionsSuppressed)
            {
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

        KeyFontSize = Math.Clamp(21 * compactness, 12, 16);
        StatusFontSize = Math.Clamp(12.5 * compactness, 9.5, 12.5);
        StatusDotSize = Math.Clamp(7 * compactness, 4.5, 7);
        var margin = Math.Clamp(10 * compactness, 2, 10);
        OuterMargin = new Thickness(margin);
        var shellPadding = Math.Clamp(8 * compactness, 3, 8);
        ShellPadding = new Thickness(shellPadding);
        var deckPadding = Math.Clamp(5 * compactness, 1, 5);
        DeckPadding = new Thickness(deckPadding);

        var compactHeight = height < 330;
        FooterBar.Visibility = Visibility.Visible;
        HeaderBar.Visibility = Visibility.Visible;
        HeaderBar.Margin = new Thickness(0, 0, 0, compactHeight ? 1 : 6);
        FooterBar.Margin = new Thickness(0, compactHeight ? 3 : 7, 0, 0);

        foreach (var row in KeyboardGrid.Children.OfType<Grid>())
        {
            row.Margin = GetRowMargin(Grid.GetRow(row));

            foreach (var button in row.Children.OfType<Button>())
            {
                var keyMargin = Math.Clamp(2.2 * compactness, compactHeight ? 0.45 : 1, 2.2);
                button.Margin = new Thickness(keyMargin);
                button.MinHeight = Math.Clamp(44 * compactness, compactHeight ? 0 : 24, 44);

                if (button.Tag is KeySpec key)
                {
                    button.Content = GetKeyDisplay(key);
                    button.FontFamily = GetKeyFontFamily(key);
                    button.FontSize = IsIconKey(key) ? KeyFontSize * 1.32 : KeyFontSize;
                }
            }
        }

        FooterHint.Visibility = width < 900 || height < 350
            ? Visibility.Collapsed
            : Visibility.Visible;
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
                "PageUp" => "PgUp",
                "PageDown" => "PgDn",
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

        public static void MakeNoActivate(IntPtr hwnd)
        {
            var style = GetWindowLong(hwnd, GwlExStyle);
            _ = SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);
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
