using System.Threading;

namespace Keebs.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void WindowSupportsCompactResizableLayout()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();

                Assert.Equal(600, window.MinWidth);
                Assert.Equal(245, window.MinHeight);
                Assert.Equal(System.Windows.ResizeMode.CanResize, window.ResizeMode);
                Assert.Equal(System.Windows.WindowStyle.SingleBorderWindow, window.WindowStyle);
                Assert.True(window.PredictionToggle.IsChecked);
                Assert.True(window.LearningToggle.IsChecked);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }

    [Fact]
    public void MinimumLayoutKeepsBarsVisibleAndKeysShrinkable()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow
                {
                    Width = 600,
                    Height = 245
                };

                typeof(MainWindow)
                    .GetMethod("UpdateScale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null);

                var keyButton = FindKeyButton(window, "A");

                Assert.Equal(System.Windows.Visibility.Visible, window.HeaderBar.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, window.FooterBar.Visibility);
                Assert.InRange(keyButton.MinHeight, 0, 23);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }

    [Fact]
    public void ShiftUpdatesTypingKeyLabels()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();

                var q = FindKeyButton(window, "Q");
                var d1 = FindKeyButton(window, "D1");
                var slash = FindKeyButton(window, "Slash");
                var shift = FindKeyButton(window, "ShiftLeft");
                var backspace = FindKeyButton(window, "Backspace");
                var win = FindKeyButton(window, "WinLeft");
                var menu = FindKeyButton(window, "Menu");
                var left = FindKeyButton(window, "Left");

                Assert.Equal("q", q.Content);
                Assert.Equal("1", d1.Content);
                Assert.Equal("/", slash.Content);
                Assert.Equal("⌫", backspace.Content);
                Assert.Equal("⊞", win.Content);
                Assert.Equal("☰", menu.Content);
                Assert.Equal("←", left.Content);

                shift.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal("Q", q.Content);
                Assert.Equal("!", d1.Content);
                Assert.Equal("?", slash.Content);
                Assert.Equal("⇧", shift.Content);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }

    [Fact]
    public void PhysicalKeyboardTypingLearnsCommittedWords()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var engine = new TextPredictionEngine(GetProfilePath());
                var window = new MainWindow(engine);

                foreach (var character in "quincboard")
                {
                    window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                        (uint)char.ToUpperInvariant(character),
                        character.ToString(),
                        isAcceptFirstPredictionChord: false));
                }

                window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                    (uint)VirtualKey.Space,
                    " ",
                    isAcceptFirstPredictionChord: false));

                var suggestions = engine.GetSuggestions(new PredictionContext("quin", [])).ToArray();

                Assert.Equal("quincboard", suggestions[0]);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }

    [Fact]
    public void PhysicalControlBackspaceKeepsPredictionSessionInParity()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()));

                foreach (var character in "hello world")
                {
                    window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                        (uint)char.ToUpperInvariant(character),
                        character.ToString(),
                        isAcceptFirstPredictionChord: false));
                }

                window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                    (uint)VirtualKey.Back,
                    string.Empty,
                    isAcceptFirstPredictionChord: false,
                    control: true));

                Assert.Equal(string.Empty, GetPredictionContext(window).CurrentWord);
                Assert.Equal("hello", GetPredictionContext(window).PreviousWord);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }

    [Fact]
    public void PhysicalSelectionDeleteClearsStalePredictionContext()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()));

                foreach (var character in "hello world")
                {
                    window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                        (uint)char.ToUpperInvariant(character),
                        character.ToString(),
                        isAcceptFirstPredictionChord: false));
                }

                window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                    (uint)VirtualKey.Left,
                    string.Empty,
                    isAcceptFirstPredictionChord: false,
                    shift: true));
                window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                    (uint)VirtualKey.Back,
                    string.Empty,
                    isAcceptFirstPredictionChord: false));

                Assert.Equal(string.Empty, GetPredictionContext(window).CurrentWord);
                Assert.Equal(string.Empty, GetPredictionContext(window).PreviousWord);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            throw threadException;
        }
    }

    private static System.Windows.Controls.Button FindKeyButton(MainWindow window, string keyId)
    {
        return window.KeyboardGrid.Children
            .OfType<System.Windows.Controls.Grid>()
            .SelectMany(row => row.Children.OfType<System.Windows.Controls.Button>())
            .Single(button => button.Tag?.GetType().GetProperty("Id")?.GetValue(button.Tag) as string == keyId);
    }

    private static PredictionContext GetPredictionContext(MainWindow window)
    {
        var textSession = typeof(MainWindow)
            .GetField("_textSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window) as TextSession;

        return textSession!.Context;
    }

    private static string GetProfilePath()
    {
        return Path.Combine(Path.GetTempPath(), "Keebs.Tests", $"{Guid.NewGuid():N}", "prediction-profile.json");
    }
}
