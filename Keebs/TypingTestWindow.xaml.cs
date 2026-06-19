using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Keebs;

public partial class TypingTestWindow : Window
{
    private static readonly string[] Prompts =
    [
        "this fires ok ish when the first guess stays visible",
        "is it on or is it only almost on",
        "now we should know if short words drift",
        "pretty little paths should forgive messy fingers",
        "try typing this sentence with normal corrections",
        "the update button should not interrupt prediction",
        "compare fragile swipe traces against saved typing runs",
        "local training data should stay private and useful",
        "i am testing contractions because they are common",
        "we will tune scores after another noisy sample"
    ];

    private readonly TypingRunRecorder _recorder;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private int _promptIndex;
    private TypingRun? _lastRun;

    public TypingTestWindow()
        : this(new TypingRunRecorder())
    {
    }

    internal TypingTestWindow(TypingRunRecorder recorder)
    {
        _recorder = recorder;
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _timer.Tick += (_, _) => RefreshStats();

        Loaded += (_, _) =>
        {
            LoadPrompt();
            InputBox.Focus();
        };
    }

    private string CurrentPrompt => Prompts[_promptIndex];

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_stopwatch.IsRunning && InputBox.Text.Length > 0)
        {
            _stopwatch.Start();
            _timer.Start();
        }

        RefreshStats();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentRun();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentRun();
        _promptIndex = (_promptIndex + 1) % Prompts.Length;
        LoadPrompt();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var path = _recorder.RunsPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(directory) ? path : directory,
            UseShellExecute = true
        });
    }

    private void LoadPrompt()
    {
        _timer.Stop();
        _stopwatch.Reset();
        _lastRun = null;
        PromptText.Text = CurrentPrompt;
        PromptCounter.Text = $"{_promptIndex + 1}/{Prompts.Length}";
        InputBox.Clear();
        InputBox.Focus();
        RefreshStats();
        StatusText.Text = $"Runs save to {_recorder.RunsPath}";
    }

    private void SaveCurrentRun()
    {
        if (InputBox.Text.Length == 0)
        {
            StatusText.Text = "Type the prompt before saving a run.";
            InputBox.Focus();
            return;
        }

        _timer.Stop();
        _stopwatch.Stop();

        var run = TypingRunMetrics.CreateRun(CurrentPrompt, InputBox.Text, _stopwatch.Elapsed, DateTimeOffset.Now);
        _recorder.Append(run);
        _lastRun = run;
        RefreshStats();
        StatusText.Text = $"Saved run to {_recorder.RunsPath}";
        InputBox.Focus();
    }

    private void RefreshStats()
    {
        var elapsed = _stopwatch.IsRunning || _stopwatch.Elapsed > TimeSpan.Zero
            ? _stopwatch.Elapsed
            : TimeSpan.Zero;
        var run = TypingRunMetrics.CreateRun(CurrentPrompt, InputBox.Text, elapsed, DateTimeOffset.Now);

        WpmText.Text = $"WPM {run.WordsPerMinute:0}";
        AccuracyText.Text = $"Acc {run.Accuracy:P0}";
        ErrorsText.Text = $"Edit {run.EditDistance}";
        ElapsedText.Text = $"Time {run.ElapsedSeconds:0.0}s";

        if (_lastRun is not null && !_stopwatch.IsRunning)
        {
            WpmText.Text = $"WPM {_lastRun.WordsPerMinute:0}";
            AccuracyText.Text = $"Acc {_lastRun.Accuracy:P0}";
            ErrorsText.Text = $"Edit {_lastRun.EditDistance}";
            ElapsedText.Text = $"Time {_lastRun.ElapsedSeconds:0.0}s";
        }
    }
}
