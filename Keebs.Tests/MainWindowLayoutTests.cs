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
                Assert.Equal(window.MinWidth, window.Width);
                Assert.Equal(window.MinHeight, window.Height);
                Assert.Equal(System.Windows.ResizeMode.CanResize, window.ResizeMode);
                Assert.Equal(System.Windows.WindowStyle.SingleBorderWindow, window.WindowStyle);
                Assert.True(window.PredictionToggle.IsChecked);
                Assert.True(window.LearningToggle.IsChecked);
                Assert.Equal("Test", window.TypingTestButton.Content);
                Assert.Equal(5, System.Windows.Controls.Grid.GetColumn(window.UpdateButton));
                Assert.Equal(TimeSpan.FromHours(1), MainWindow.AutomaticUpdateCheckInterval);

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
    public void UpdateIndicatorReflectsReleaseAvailability()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false);
                var currentVersion = new Version(1, 2, 3);
                var latestVersion = new Version(1, 2, 4);

                window.ApplyUpdateAvailability(new UpdateCheckResult(
                    true,
                    currentVersion,
                    latestVersion,
                    "https://example.test/keebs.msi",
                    "https://example.test/release",
                    "Update available"));

                Assert.Equal(System.Windows.Visibility.Visible, window.UpdateAvailableIndicator.Visibility);
                Assert.Contains(latestVersion.ToString(), Assert.IsType<string>(window.UpdateButton.ToolTip));

                window.ApplyUpdateAvailability(new UpdateCheckResult(
                    false,
                    latestVersion,
                    latestVersion,
                    null,
                    null,
                    "Up to date"));

                Assert.Equal(System.Windows.Visibility.Collapsed, window.UpdateAvailableIndicator.Visibility);
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
    public void ChassisDragExcludesInteractiveControls()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false);

                Assert.False(MainWindow.IsInteractiveElement(window.Chassis));
                Assert.True(MainWindow.IsInteractiveElement(FindKeyButton(window, "A")));
                Assert.True(MainWindow.IsInteractiveElement(window.UpdateButton));

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
    public void RightAndMiddleButtonsMapToControlAndAltChords()
    {
        Assert.Equal(VirtualKey.Control, MainWindow.GetPointerChordModifier(System.Windows.Input.MouseButton.Right));
        Assert.Equal(VirtualKey.Alt, MainWindow.GetPointerChordModifier(System.Windows.Input.MouseButton.Middle));
        Assert.Null(MainWindow.GetPointerChordModifier(System.Windows.Input.MouseButton.Left));
    }

    [Fact]
    public void PointerModifiedKeyPressSendsChordWithoutTypingPredictionText()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false);
                var keyButton = FindKeyButton(window, "A");
                IReadOnlyList<VirtualKey>? sentModifiers = null;
                VirtualKey? sentKey = null;
                KeyboardInput.SendVirtualKeyChordOverride = (modifiers, key) =>
                {
                    sentModifiers = modifiers.ToArray();
                    sentKey = key;
                };

                try
                {
                    typeof(MainWindow)
                        .GetMethod("PressKeyWithPointerModifier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, [keyButton.Tag, VirtualKey.Control]);

                    Assert.Equal([VirtualKey.Control], sentModifiers);
                    Assert.Equal(VirtualKey.A, sentKey);
                    Assert.Equal(string.Empty, GetPredictionContext(window).CurrentWord);
                }
                finally
                {
                    KeyboardInput.SendVirtualKeyChordOverride = null;
                    window.Close();
                }
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
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false)
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
    public void HorizontalResizeDoesNotIncreaseKeyHeight()
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

                var updateScale = typeof(MainWindow)
                    .GetMethod("UpdateScale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                updateScale.Invoke(window, null);
                var keyButton = FindKeyButton(window, "A");
                var compactMinHeight = keyButton.MinHeight;
                var compactVerticalMargin = keyButton.Margin.Top;
                var compactDeckHeight = window.KeyboardDeck.Height;
                var compactFontSize = window.KeyFontSize;

                window.Width = 950;
                updateScale.Invoke(window, null);

                Assert.Equal(compactMinHeight, keyButton.MinHeight, precision: 3);
                Assert.Equal(compactVerticalMargin, keyButton.Margin.Top, precision: 3);
                Assert.Equal(compactDeckHeight, window.KeyboardDeck.Height, precision: 3);
                Assert.True(window.KeyFontSize >= compactFontSize);

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
    public void HorizontalResizeDoesNotIncreaseRenderedKeyHeight()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false)
                {
                    Width = 600,
                    Height = 245
                };

                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);
                window.UpdateLayout();

                var keyButton = FindKeyButton(window, "A");
                var compactHeight = keyButton.ActualHeight;
                var compactDeckHeight = window.KeyboardDeck.ActualHeight;

                window.Width = 1400;
                window.UpdateLayout();
                window.Dispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);
                window.UpdateLayout();

                Assert.InRange(keyButton.ActualHeight, 0, compactHeight + 0.5);
                Assert.InRange(window.KeyboardDeck.ActualHeight, 0, compactDeckHeight + 0.5);
                Assert.True(window.KeyFontSize >= 13.5);

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
    public void CompactLayoutKeepsSpacebarAboveFooterWhenPredictionsAreVisible()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false)
                {
                    Width = 600,
                    Height = 245
                };

                window.Show();
                window.UpdateLayout();
                PressPhysicalText(window, "show");
                window.Dispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);
                window.UpdateLayout();

                var space = FindKeyButton(window, "Space");
                var spaceBottom = space.TranslatePoint(
                    new System.Windows.Point(0, space.ActualHeight),
                    window.RootGrid).Y;
                var deckBottom = window.KeyboardDeck.TranslatePoint(
                    new System.Windows.Point(0, window.KeyboardDeck.ActualHeight),
                    window.RootGrid).Y;
                var footerTop = window.FooterBar.TranslatePoint(
                    new System.Windows.Point(0, 0),
                    window.RootGrid).Y;

                Assert.Equal(System.Windows.Visibility.Visible, window.HeaderBar.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, window.FooterBar.Visibility);
                Assert.NotEmpty(GetDisplayedSuggestions(window));
                Assert.True(space.ActualHeight > 0);
                Assert.True(spaceBottom <= footerTop, $"space bottom {spaceBottom:0.0} exceeded footer top {footerTop:0.0}");
                Assert.True(deckBottom <= footerTop, $"deck bottom {deckBottom:0.0} exceeded footer top {footerTop:0.0}");

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
    public void CompactNavigationLabelsFitInsideTheirKeys()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false)
                {
                    Width = 600,
                    Height = 245
                };

                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);
                window.UpdateLayout();

                string[] compactKeyIds =
                [
                    "PrintScreen", "ScrollLock", "Pause",
                    "Insert", "Home", "PageUp",
                    "Delete", "End", "PageDown"
                ];

                foreach (var keyId in compactKeyIds)
                {
                    var button = FindKeyButton(window, keyId);
                    var label = new System.Windows.Controls.TextBlock
                    {
                        Text = Assert.IsType<string>(button.Content),
                        FontFamily = button.FontFamily,
                        FontSize = button.FontSize,
                        FontStyle = button.FontStyle,
                        FontWeight = button.FontWeight,
                        FontStretch = button.FontStretch
                    };
                    label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

                    var availableWidth = button.ActualWidth - button.Padding.Left - button.Padding.Right - 4;
                    Assert.True(
                        label.DesiredSize.Width <= availableWidth,
                        $"{keyId} label width {label.DesiredSize.Width:0.0} exceeded {availableWidth:0.0}");
                }

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
    public void CompactIconLabelsStaySmallerThanTextLegends()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false)
                {
                    Width = 600,
                    Height = 245
                };

                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);
                window.UpdateLayout();

                string[] iconKeyIds =
                [
                    "Backspace", "Tab", "CapsLock", "ShiftLeft", "ShiftRight", "Enter",
                    "WinLeft", "WinRight", "Menu", "Left", "Up", "Down", "Right"
                ];

                foreach (var keyId in iconKeyIds)
                {
                    var button = FindKeyButton(window, keyId);
                    Assert.True(
                        button.FontSize < window.KeyFontSize,
                        $"{keyId} icon size {button.FontSize:0.0} should be below text size {window.KeyFontSize:0.0}");
                    Assert.True(
                        button.FontSize <= button.ActualHeight * 0.7,
                        $"{keyId} icon size {button.FontSize:0.0} was too large for key height {button.ActualHeight:0.0}");
                }

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
    public void PhysicalSshCommandSuppressesPasswordLearningAndBackspacePredictionUpdates()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var engine = GetPredictionEngine(window);

                try
                {
                    PressPhysicalText(window, "ssh robokrabs@robokrabs");
                    PressPhysicalKey(window, VirtualKey.Enter);

                    Assert.True(IsPredictionsSuppressed(window));
                    Assert.Equal(string.Empty, GetPredictionContext(window).CurrentWord);
                    Assert.Empty(GetDisplayedSuggestions(window));

                    PressPhysicalText(window, "zzsecrets");

                    for (var index = 0; index < 20; index++)
                    {
                        PressPhysicalKey(window, VirtualKey.Back);
                    }

                    Assert.True(IsPredictionsSuppressed(window));
                    Assert.Equal(string.Empty, GetPredictionContext(window).CurrentWord);
                    Assert.Empty(GetDisplayedSuggestions(window));

                    PressPhysicalKey(window, VirtualKey.Enter);

                    Assert.False(IsPredictionsSuppressed(window));
                    Assert.DoesNotContain("zzsecrets", engine.GetSuggestions(new PredictionContext("zzsec", [])));
                }
                finally
                {
                    window.Close();
                }
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
    public void PhysicalTextKeysAreIgnoredWhenFocusedElementIsNotTextInput()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()));

                typeof(MainWindow)
                    .GetField("_focusedTextInputActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(window, false);

                var shouldProcess = typeof(MainWindow)
                    .GetMethod("ShouldProcessPhysicalKeyboardEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                var movementKey = new PhysicalKeyPressedEventArgs(
                    (uint)VirtualKey.W,
                    "w",
                    isAcceptFirstPredictionChord: false);
                var submittedSensitivePrompt = new PhysicalKeyPressedEventArgs(
                    (uint)VirtualKey.Enter,
                    string.Empty,
                    isAcceptFirstPredictionChord: false);

                Assert.False((bool)shouldProcess.Invoke(window, [movementKey])!);

                typeof(MainWindow)
                    .GetField("_focusedTextContextSensitive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(window, true);

                Assert.True((bool)shouldProcess.Invoke(window, [submittedSensitivePrompt])!);

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

    [Fact]
    public void OneKeySwipeGestureFallsBackToTap()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(new TextPredictionEngine(GetProfilePath()))
                {
                    Width = 1000,
                    Height = 360
                };

                window.Show();
                window.UpdateLayout();

                var q = FindKeyButton(window, "Q");
                var key = q.Tag!;
                var center = q.TranslatePoint(
                    new System.Windows.Point(q.ActualWidth / 2, q.ActualHeight / 2),
                    window.KeyboardGrid);

                typeof(MainWindow)
                    .GetField("_pointerDownKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(window, key);

                var swipeLetters = (List<char>)typeof(MainWindow)
                    .GetField("_swipeLetters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(window)!;

                swipeLetters.Add('q');

                var shouldFallback = (bool)typeof(MainWindow)
                    .GetMethod("ShouldFallbackSwipeToTap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [center])!;

                swipeLetters.Add('w');

                var shouldNotFallback = (bool)typeof(MainWindow)
                    .GetMethod("ShouldFallbackSwipeToTap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [center])!;

                Assert.True(shouldFallback);
                Assert.False(shouldNotFallback);

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

    [Theory]
    [InlineData("the")]
    [InlineData("quick")]
    [InlineData("keyboard")]
    public void IdealSwipePathResolvesExpectedWord(string word)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedSwipePath(window, word);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, word, suggestion);

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

    [Theory]
    [InlineData("the")]
    [InlineData("quick")]
    [InlineData("keyboard")]
    [InlineData("prediction")]
    [InlineData("tomorrow")]
    [InlineData("installation")]
    public void InterpolatedSwipePathResolvesExpectedWord(string word)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedInterpolatedSwipePath(window, word, noiseScale: 0, addNeighborPollution: false);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, word, suggestion);

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

    [Theory]
    [InlineData("keyboard")]
    [InlineData("prediction")]
    [InlineData("tomorrow")]
    [InlineData("installation")]
    public void NoisySwipePathResolvesExpectedWord(string word)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedInterpolatedSwipePath(window, word, noiseScale: 0.18, addNeighborPollution: false);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, word, suggestion);

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

    [Theory]
    [InlineData("keyboard")]
    [InlineData("prediction")]
    [InlineData("tomorrow")]
    [InlineData("installation")]
    public void PollutedSwipePathResolvesExpectedWord(string word)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedInterpolatedSwipePath(window, word, noiseScale: 0.12, addNeighborPollution: true);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, word, suggestion);

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
    public void NoisySwipePathResolvesCurrentSentenceWords()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var getBestSwipeSuggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                foreach (var word in new[]
                         {
                             "this", "actually", "working", "pretty", "think",
                             "still", "little", "touchy", "try", "sentence",
                             "typing", "right", "ok", "on", "now"
                         })
                {
                    SeedInterpolatedSwipePath(window, word, noiseScale: 0.14, addNeighborPollution: true);

                    var suggestion = getBestSwipeSuggestion.Invoke(window, null) as string;

                    AssertSwipeSuggestion(window, word, suggestion);
                }

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

    [Theory]
    [InlineData(0.00, 0.00)]
    [InlineData(0.10, 0.35)]
    [InlineData(0.18, 0.55)]
    [InlineData(-0.10, -0.35)]
    [InlineData(-0.18, -0.55)]
    [InlineData(0.24, -0.45)]
    [InlineData(-0.24, 0.45)]
    public void PrettySwipePathDoesNotResolveToPottery(double noiseScale, double pollutionScale)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedInterpolatedSwipePath(window, "pretty", noiseScale, pollutionScale);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, "pretty", suggestion);

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

    [Theory]
    [InlineData("teh", "the")]
    [InlineData("its", "is")]
    [InlineData("iuds", "is")]
    [InlineData("oik", "ok")]
    [InlineData("oh", "on")]
    [InlineData("quik", "quick")]
    public void SwipePathToleratesMinorTraceMistakes(string trace, string expectedWord)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedSwipePath(window, trace);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, expectedWord, suggestion);

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

    [Theory]
    [InlineData("iuhygfds", "is")]
    [InlineData("oiuytr", "or")]
    [InlineData("iuytres", "is")]
    [InlineData("sdfghjkpiulgfd", "should")]
    [InlineData("mjhgtresdrty", "messy")]
    [InlineData("ftyuijnhgfderds", "fingers")]
    [InlineData("cfvghjoutredcrtyuiokjnbgfds", "corrections")]
    [InlineData("cfghuioutredcrtyokjnbfds", "corrections")]
    [InlineData("tredsxdftyioimnbg", "testing")]
    [InlineData("cvghiokjntreaxdtyuiokjnbfds", "contractions")]
    [InlineData("asdftrer", "after")]
    [InlineData("asdfvbnjoyhrer", "another")]
    [InlineData("sasdcvbnmkopltre", "sample")]
    [InlineData("sasdfbnmoplktre", "sample")]
    public void SwipePathResolvesFreshHarnessProblemTraces(string trace, string expectedWord)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedSwipePath(window, trace);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, expectedWord, suggestion);

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

    [Theory]
    [InlineData("now w", "sdfghjoiyiokrd", " should ")]
    [InlineData("swipe traces ", "asdfgdsaguihbcsrt", "against ")]
    [InlineData("saved typing runs ", "loijhbcxcxsfhmkl", "local ")]
    [InlineData("stay private ", "asdfvbhnfrs", "and ")]
    [InlineData("they are ", "cvbhjiopojmgergn", "common ")]
    public void CommittedSwipeUsesContextForAmbiguousHarnessTraces(
        string textBeforeCaret,
        string trace,
        string expectedSentText)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;

                try
                {
                    SeedTextSession(window, textBeforeCaret);
                    SeedSwipePath(window, trace);

                    var committed = (bool)typeof(MainWindow)
                        .GetMethod("TryCommitSwipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, null)!;

                    Assert.True(committed);
                    Assert.Equal(expectedSentText, sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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
    public void CommittedSwipeSendsResolvedWord()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;

                try
                {
                    SeedSwipePath(window, "the");

                    var committed = (bool)typeof(MainWindow)
                        .GetMethod("TryCommitSwipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, null)!;

                    Assert.True(committed);
                    Assert.Equal("the ", sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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
    public void CommittedSwipeUsesFirstVisibleSwipeSuggestion()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;

                try
                {
                    SeedSwipePath(window, "asdftrer");

                    typeof(MainWindow)
                        .GetMethod("RefreshSwipeSuggestions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, null);

                    var suggestions = (IReadOnlyList<string>)typeof(MainWindow)
                        .GetField("_suggestions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .GetValue(window)!;

                    Assert.NotEmpty(suggestions);
                    var visibleFirst = suggestions[0];

                    var committed = (bool)typeof(MainWindow)
                        .GetMethod("TryCommitSwipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, null)!;

                    Assert.True(committed);
                    Assert.Equal($"{visibleFirst} ", sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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
    public void CommittedSwipesSendMultipleWords()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;

                try
                {
                    foreach (var word in new[] { "the", "quick", "zebra", "swipe" })
                    {
                        SeedSwipePath(window, word);

                        var committed = (bool)typeof(MainWindow)
                            .GetMethod("TryCommitSwipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(window, null)!;

                        Assert.True(committed);
                    }

                    Assert.Equal("the quick zebra swipe ", sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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
    public void SwipeAfterTypedWordInsertsWordBoundary()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;

                try
                {
                    var sendTextKey = typeof(MainWindow)
                        .GetMethod("SendTextKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    var aKey = FindKeyButton(window, "A").Tag;

                    sendTextKey.Invoke(window, [aKey]);
                    SeedInterpolatedSwipePath(window, "little", noiseScale: 0.14, addNeighborPollution: true);

                    var committed = (bool)typeof(MainWindow)
                        .GetMethod("TryCommitSwipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, null)!;

                    Assert.True(committed);
                    Assert.Equal("a little ", sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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
    public void SwipeAfterTypedPunctuationInsertsWordBoundary()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;

                try
                {
                    var sendTextKey = typeof(MainWindow)
                        .GetMethod("SendTextKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    var butKeys = new[] { "B", "U", "T", "Period" }
                        .Select(id => FindKeyButton(window, id).Tag)
                        .ToArray();

                    foreach (var key in butKeys)
                    {
                        sendTextKey.Invoke(window, [key]);
                    }

                    SeedInterpolatedSwipePath(window, "try", noiseScale: 0.14, addNeighborPollution: true);

                    var committed = (bool)typeof(MainWindow)
                        .GetMethod("TryCommitSwipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(window, null)!;

                    Assert.True(committed);
                    Assert.Equal("but. try ", sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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

    [Theory]
    [InlineData("is")]
    [InlineData("ok")]
    [InlineData("on")]
    public void ShortCommonSwipePathResolvesExpectedWord(string word)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedInterpolatedSwipePath(window, word, noiseScale: 0.12, addNeighborPollution: true);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, word, suggestion);

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

    [Theory]
    [InlineData(0.00, 0.00)]
    [InlineData(0.14, 0.55)]
    [InlineData(-0.14, -0.55)]
    [InlineData(0.22, -0.45)]
    [InlineData(-0.22, 0.45)]
    public void ThisSwipePathDoesNotResolveToToss(double noiseScale, double pollutionScale)
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();

                SeedInterpolatedSwipePath(window, "this", noiseScale, pollutionScale);

                var suggestion = typeof(MainWindow)
                    .GetMethod("GetBestSwipeSuggestion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null) as string;

                AssertSwipeSuggestion(window, "this", suggestion);

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
    public void SwipeGestureUpdatesPredictionsBeforeRelease()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var begin = typeof(MainWindow)
                    .GetMethod("BeginPointerGesture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var move = typeof(MainWindow)
                    .GetMethod("ContinuePointerGesture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                begin.Invoke(window, [GetKeyCenter(window, "T"), CreateGestureEventArgs()]);
                move.Invoke(window, [GetKeyCenter(window, "H"), CreateGestureEventArgs()]);
                move.Invoke(window, [GetKeyCenter(window, "E"), CreateGestureEventArgs()]);

                Assert.Contains("the", window.SuggestionStrip.Items.Cast<string>());

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
    public void SwipeGestureCollectsNearestLettersBetweenButtons()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var addSwipeKeyAt = typeof(MainWindow)
                    .GetMethod("AddSwipeKeyAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var swipeLetters = (List<char>)typeof(MainWindow)
                    .GetField("_swipeLetters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(window)!;

                var q = GetKeyCenter(window, "Q");
                var w = GetKeyCenter(window, "W");
                var nearWGap = new System.Windows.Point(
                    q.X + ((w.X - q.X) * 0.72),
                    q.Y + ((w.Y - q.Y) * 0.72));

                addSwipeKeyAt.Invoke(window, [q]);
                addSwipeKeyAt.Invoke(window, [nearWGap]);

                Assert.Equal(['q', 'w'], swipeLetters);

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
    public void TwoLetterDragDoesNotEnterSwipeFailureMode()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = CreateLaidOutWindow();
                var sentText = string.Empty;
                KeyboardInput.SendTextOverride = text => sentText += text;
                var begin = typeof(MainWindow)
                    .GetMethod("BeginPointerGesture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var move = typeof(MainWindow)
                    .GetMethod("ContinuePointerGesture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var end = typeof(MainWindow)
                    .GetMethod("EndPointerGesture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                try
                {
                    begin.Invoke(window, [GetKeyCenter(window, "S"), CreateGestureEventArgs()]);
                    move.Invoke(window, [GetKeyCenter(window, "O"), CreateGestureEventArgs()]);
                    end.Invoke(window, [GetKeyCenter(window, "O"), CreateGestureEventArgs()]);

                    Assert.Equal("s", sentText);
                }
                finally
                {
                    KeyboardInput.SendTextOverride = null;
                    window.Close();
                }
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

    private static MainWindow CreateLaidOutWindow()
    {
        var window = new MainWindow(new TextPredictionEngine(GetProfilePath()), startFocusMonitors: false)
        {
            Width = 1000,
            Height = 360
        };

        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static System.Windows.Point GetKeyCenter(MainWindow window, string keyId)
    {
        var key = FindKeyButton(window, keyId);
        return key.TranslatePoint(
            new System.Windows.Point(key.ActualWidth / 2, key.ActualHeight / 2),
            window.KeyboardGrid);
    }

    private static System.Windows.RoutedEventArgs CreateGestureEventArgs()
    {
        return new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
    }

    private static void SeedSwipePath(MainWindow window, string trace)
    {
        var swipeLetters = (List<char>)typeof(MainWindow)
            .GetField("_swipeLetters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        var swipePoints = (List<System.Windows.Point>)typeof(MainWindow)
            .GetField("_swipePoints", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        swipeLetters.Clear();
        swipePoints.Clear();

        foreach (var character in trace)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            var center = GetKeyCenter(window, char.ToUpperInvariant(character).ToString());

            if (swipeLetters.Count == 0 || swipeLetters[^1] != char.ToLowerInvariant(character))
            {
                swipeLetters.Add(char.ToLowerInvariant(character));
            }

            swipePoints.Add(center);
        }
    }

    private static void SeedTextSession(MainWindow window, string textBeforeCaret)
    {
        var textSession = (TextSession)typeof(MainWindow)
            .GetField("_textSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        textSession.SeedFromTextBeforeCaret(textBeforeCaret);
    }

    private static void SeedInterpolatedSwipePath(
        MainWindow window,
        string word,
        double noiseScale,
        bool addNeighborPollution)
    {
        SeedInterpolatedSwipePath(window, word, noiseScale, addNeighborPollution ? 0.55 : 0);
    }

    private static void SeedInterpolatedSwipePath(
        MainWindow window,
        string word,
        double noiseScale,
        double pollutionScale)
    {
        var swipeLetters = (List<char>)typeof(MainWindow)
            .GetField("_swipeLetters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        var swipePoints = (List<System.Windows.Point>)typeof(MainWindow)
            .GetField("_swipePoints", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        var addSwipeKeyAt = typeof(MainWindow)
            .GetMethod("AddSwipeKeyAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var keyUnit = GetAverageLetterKeySize(window);
        var uniqueLetters = NormalizeSwipeLetters(word);
        var centers = uniqueLetters
            .Select(letter => GetKeyCenter(window, char.ToUpperInvariant(letter).ToString()))
            .ToArray();

        swipeLetters.Clear();
        swipePoints.Clear();

        for (var index = 0; index < centers.Length; index++)
        {
            AddSwipeSample(window, centers[index], addSwipeKeyAt);

            if (index == centers.Length - 1)
            {
                continue;
            }

            var start = centers[index];
            var end = centers[index + 1];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            var perpendicularX = length <= 0 ? 0 : -dy / length;
            var perpendicularY = length <= 0 ? 0 : dx / length;
            var samples = Math.Max(2, (int)Math.Ceiling(length / (keyUnit * 0.45)));

            for (var sample = 1; sample < samples; sample++)
            {
                var ratio = (double)sample / samples;
                var wave = ((sample + index) % 2 == 0 ? 1 : -1) * noiseScale * keyUnit;
                var point = new System.Windows.Point(
                    start.X + (dx * ratio) + (perpendicularX * wave),
                    start.Y + (dy * ratio) + (perpendicularY * wave));

                AddSwipeSample(window, point, addSwipeKeyAt);

                if (pollutionScale != 0 && sample == samples / 2)
                {
                    var polluted = new System.Windows.Point(
                        point.X + (perpendicularX * keyUnit * pollutionScale),
                        point.Y + (perpendicularY * keyUnit * pollutionScale));
                    AddSwipeSample(window, polluted, addSwipeKeyAt);
                }
            }
        }
    }

    private static void AddSwipeSample(
        MainWindow window,
        System.Windows.Point point,
        System.Reflection.MethodInfo addSwipeKeyAt)
    {
        var swipePoints = (List<System.Windows.Point>)typeof(MainWindow)
            .GetField("_swipePoints", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        swipePoints.Add(point);
        addSwipeKeyAt.Invoke(window, [point]);
    }

    private static void AssertSwipeSuggestion(MainWindow window, string expected, string? actual)
    {
        if (actual == expected)
        {
            return;
        }

        var swipeLetters = (List<char>)typeof(MainWindow)
            .GetField("_swipeLetters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        var engine = (TextPredictionEngine)typeof(MainWindow)
            .GetField("_predictionEngine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

        var trace = new string([.. swipeLetters]);
        var candidates = engine.GetSwipeCandidates(trace, GetPredictionContext(window), 12).ToArray();
        var scoreMethod = typeof(MainWindow)
            .GetMethod("GetSwipeCandidateScore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var keyUnit = GetAverageLetterKeySize(window);
        var scoredCandidates = candidates.Select((candidate, index) =>
        {
            var score = scoreMethod.Invoke(window, [candidate, keyUnit, index]);
            return $"{candidate}:{(score is double value ? value.ToString("0.000") : "null")}";
        });

        Assert.Fail(
            $"Expected '{expected}', got '{actual ?? "<null>"}'. Trace='{trace}'. Candidates=[{string.Join(", ", scoredCandidates)}]");
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

    private static double GetAverageLetterKeySize(MainWindow window)
    {
        var sizes = window.KeyboardGrid.Children
            .OfType<System.Windows.Controls.Grid>()
            .SelectMany(row => row.Children.OfType<System.Windows.Controls.Button>())
            .Where(button =>
            {
                var keyText = button.Tag?.GetType().GetProperty("Text")?.GetValue(button.Tag) as string;
                return keyText is { Length: 1 } && char.IsLetter(keyText[0]);
            })
            .Select(button => Math.Min(button.ActualWidth, button.ActualHeight))
            .Where(size => size > 0)
            .Order()
            .ToArray();

        return sizes.Length == 0 ? 1 : sizes[sizes.Length / 2];
    }

    private static PredictionContext GetPredictionContext(MainWindow window)
    {
        var textSession = typeof(MainWindow)
            .GetField("_textSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window) as TextSession;

        return textSession!.Context;
    }

    private static TextPredictionEngine GetPredictionEngine(MainWindow window)
    {
        return (TextPredictionEngine)typeof(MainWindow)
            .GetField("_predictionEngine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;
    }

    private static IReadOnlyCollection<string> GetDisplayedSuggestions(MainWindow window)
    {
        return (IReadOnlyCollection<string>)typeof(MainWindow)
            .GetField("_suggestions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;
    }

    private static bool IsPredictionsSuppressed(MainWindow window)
    {
        return (bool)typeof(MainWindow)
            .GetProperty("PredictionsSuppressed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;
    }

    private static void PressPhysicalText(MainWindow window, string text)
    {
        foreach (var character in text)
        {
            window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
                (uint)char.ToUpperInvariant(character),
                character.ToString(),
                isAcceptFirstPredictionChord: false));
        }
    }

    private static void PressPhysicalKey(MainWindow window, VirtualKey key)
    {
        window.ApplyPhysicalKeyToPredictionSession(new PhysicalKeyPressedEventArgs(
            (uint)key,
            string.Empty,
            isAcceptFirstPredictionChord: false));
    }

    private static string GetProfilePath()
    {
        return Path.Combine(Path.GetTempPath(), "Keebs.Tests", $"{Guid.NewGuid():N}", "prediction-profile.json");
    }
}
