using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Keebs;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<string> _suggestions = [];
    private readonly TextPredictionEngine _predictionEngine = new();
    private readonly TextSession _textSession = new();
    private readonly SensitiveInputMonitor _sensitiveInputMonitor = new();
    private bool _shift;
    private bool _privateMode;
    private double _keyFontSize = 16;
    private double _statusFontSize = 12;
    private Thickness _outerMargin = new(12);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        SuggestionStrip.ItemsSource = _suggestions;
        BuildKeyboard();

        _sensitiveInputMonitor.StateChanged += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                RefreshPrivacyState();
                RefreshSuggestions();
            });
        Loaded += (_, _) =>
        {
            _sensitiveInputMonitor.Start();
            RefreshPrivacyState();
            RefreshSuggestions();
            UpdateScale();
        };
        Closed += (_, _) => _sensitiveInputMonitor.Dispose();
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

    public Thickness OuterMargin
    {
        get => _outerMargin;
        private set => SetField(ref _outerMargin, value);
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
        AddRow(0, ["q", "w", "e", "r", "t", "y", "u", "i", "o", "p"]);
        AddRow(1, ["a", "s", "d", "f", "g", "h", "j", "k", "l"]);
        AddRow(2, ["Shift", "z", "x", "c", "v", "b", "n", "m", "Back"]);
        AddRow(3, ["Tab", "Space", ".", ",", "'", "Enter"]);
    }

    private void AddRow(int rowIndex, IReadOnlyList<string> labels)
    {
        var row = new UniformGrid
        {
            Rows = 1,
            Columns = labels.Count,
            Margin = GetRowMargin(rowIndex)
        };

        foreach (var label in labels)
        {
            var button = new Button
            {
                Content = label,
                Tag = label,
                Style = (Style)FindResource(label is "Shift" or "Back" or "Tab" or "Enter"
                    ? "UtilityButton"
                    : "KeyboardButton")
            };

            button.Click += Key_Click;
            row.Children.Add(button);
        }

        Grid.SetRow(row, rowIndex);
        KeyboardGrid.Children.Add(row);
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        try
        {
            switch (key)
            {
                case "Shift":
                    _shift = !_shift;
                    RefreshShiftLabels();
                    return;
                case "Back":
                    KeyboardInput.SendVirtualKey(VirtualKey.Back);
                    _textSession.Backspace();
                    break;
                case "Tab":
                    KeyboardInput.SendVirtualKey(VirtualKey.Tab);
                    _textSession.CommitBoundary();
                    break;
                case "Enter":
                    KeyboardInput.SendVirtualKey(VirtualKey.Enter);
                    _textSession.CommitBoundary();
                    break;
                case "Space":
                    KeyboardInput.SendText(" ");
                    _textSession.TypeText(" ");
                    break;
                default:
                    var text = _shift ? key.ToUpperInvariant() : key;
                    KeyboardInput.SendText(text);
                    _textSession.TypeText(text);
                    if (_shift)
                    {
                        _shift = false;
                        RefreshShiftLabels();
                    }
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            ShowInputError(ex);
            return;
        }

        RefreshSuggestions();
    }

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (PredictionsSuppressed || sender is not Button { Content: string suggestion } || string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        try
        {
            var replacement = _textSession.AcceptSuggestion(suggestion);
            for (var i = 0; i < replacement.BackspaceCount; i++)
            {
                KeyboardInput.SendVirtualKey(VirtualKey.Back);
            }

            KeyboardInput.SendText(replacement.Text);
        }
        catch (InvalidOperationException ex)
        {
            ShowInputError(ex);
            return;
        }

        _predictionEngine.LearnAcceptedSuggestion(suggestion);
        RefreshSuggestions();
    }

    private void PrivateMode_Changed(object sender, RoutedEventArgs e)
    {
        _privateMode = PrivateModeToggle.IsChecked == true;
        RefreshPrivacyState();
        RefreshSuggestions();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool PredictionsSuppressed => _privateMode || _sensitiveInputMonitor.IsSensitive;

    private void RefreshPrivacyState()
    {
        if (_privateMode)
        {
            PrivacyStatus.Text = "Private mode";
            return;
        }

        PrivacyStatus.Text = _sensitiveInputMonitor.IsSensitive
            ? "Sensitive field: raw keys"
            : "Predictions on";
    }

    private void RefreshSuggestions()
    {
        _suggestions.Clear();

        if (PredictionsSuppressed)
        {
            _textSession.ResetPredictionContext();
            return;
        }

        foreach (var suggestion in _predictionEngine.GetSuggestions(_textSession.Context).Take(3))
        {
            _suggestions.Add(suggestion);
        }
    }

    private void RefreshShiftLabels()
    {
        foreach (var button in KeyboardGrid.Children
                     .OfType<UniformGrid>()
                     .SelectMany(row => row.Children.OfType<Button>()))
        {
            if (button.Tag is not string tag || tag.Length != 1 || !char.IsLetter(tag[0]))
            {
                continue;
            }

            button.Content = _shift ? tag.ToUpperInvariant() : tag;
        }
    }

    private void ShowInputError(Exception exception)
    {
        PrivacyStatus.Text = "Input failed";
        PrivacyStatus.ToolTip = exception.Message;
    }

    private void UpdateScale()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var compactness = Math.Min(width / 980, height / 360);

        KeyFontSize = Math.Clamp(18 * compactness, 11, 18);
        StatusFontSize = Math.Clamp(12 * compactness, 9, 12);
        var margin = Math.Clamp(12 * compactness, 5, 12);
        OuterMargin = new Thickness(margin);

        foreach (var row in KeyboardGrid.Children.OfType<UniformGrid>())
        {
            row.Margin = GetRowMargin(Grid.GetRow(row));

            foreach (var button in row.Children.OfType<Button>())
            {
                var keyMargin = Math.Clamp(4 * compactness, 1.5, 4);
                button.Margin = new Thickness(keyMargin);
                button.MinHeight = Math.Clamp(46 * compactness, 24, 46);
            }
        }

        FooterHint.Visibility = width < 620 || height < 250
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private Thickness GetRowMargin(int rowIndex)
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var inset = rowIndex == 1 ? Math.Clamp(width * 0.025, 6, 24) : 0;
        return new Thickness(inset, 0, inset, 0);
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
}
