using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Keebs;

internal sealed class SensitiveInputMonitor : IDisposable
{
    private static readonly string[] SensitiveKeywords =
    [
        "password", "passcode", "pin", "cvv", "cvc", "security code", "secret",
        "otp", "one-time", "one time", "2fa", "mfa", "verification code",
        "recovery code", "ssn", "social security"
    ];

    private static readonly string[] SensitiveTokenPhrases =
    [
        "access token", "api token", "auth token", "bearer token", "personal access token",
        "refresh token", "secret token"
    ];

    private readonly AutomationFocusChangedEventHandler _focusChangedHandler;
    private int _updateInFlight;
    private int _updateRequestId;
    private bool _started;

    public SensitiveInputMonitor()
    {
        _focusChangedHandler = (_, _) => QueueUpdateFromFocusedElement();
    }

    public event EventHandler? StateChanged;

    public event EventHandler? FocusChanged;

    public bool IsSensitive { get; private set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Automation.AddAutomationFocusChangedEventHandler(_focusChangedHandler);
        QueueUpdateFromFocusedElement();
    }

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        Automation.RemoveAutomationFocusChangedEventHandler(_focusChangedHandler);
        _started = false;
    }

    private void QueueUpdateFromFocusedElement()
    {
        var requestId = Interlocked.Increment(ref _updateRequestId);
        if (Interlocked.CompareExchange(ref _updateInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() => UpdateFromFocusedElement(requestId));
    }

    private void UpdateFromFocusedElement(int requestId)
    {
        try
        {
            var sensitive = IsFocusedElementSensitive();
            if (!_started || requestId != Volatile.Read(ref _updateRequestId))
            {
                return;
            }

            var stateChanged = sensitive != IsSensitive;
            IsSensitive = sensitive;
            FocusChanged?.Invoke(this, EventArgs.Empty);

            if (!stateChanged)
            {
                return;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref _updateInFlight, 0);
        }
    }

    private static bool IsFocusedElementSensitive()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return element is not null && IsElementOrAncestorSensitive(element);
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool IsElementOrAncestorSensitive(AutomationElement element)
    {
        var current = element;

        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            if (IsElementSensitive(current))
            {
                return true;
            }

            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (ElementNotAvailableException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsElementSensitive(AutomationElement element)
    {
        if (GetBoolProperty(element, AutomationElement.IsPasswordProperty))
        {
            return true;
        }

        var userFacingMetadata = string.Join(" ",
            GetStringProperty(element, AutomationElement.NameProperty),
            GetStringProperty(element, AutomationElement.AutomationIdProperty),
            GetStringProperty(element, AutomationElement.HelpTextProperty));

        if (IsSensitiveMetadata(userFacingMetadata))
        {
            return true;
        }

        var className = GetStringProperty(element, AutomationElement.ClassNameProperty);
        return LooksLikeNativeClassName(className) &&
               SensitiveKeywords.Any(keyword => className.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsSensitiveMetadata(string metadata)
    {
        return SensitiveKeywords.Any(keyword =>
                   metadata.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               SensitiveTokenPhrases.Any(phrase =>
                   metadata.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool LooksLikeNativeClassName(string className)
    {
        return !string.IsNullOrWhiteSpace(className) &&
               className.Length <= 64 &&
               !className.Any(char.IsWhiteSpace) &&
               !className.Contains('[', StringComparison.Ordinal) &&
               !className.Contains(']', StringComparison.Ordinal) &&
               !className.Contains(':', StringComparison.Ordinal);
    }

    private static bool GetBoolProperty(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return value is bool boolValue && boolValue;
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
    }

    private static string GetStringProperty(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(property, ignoreDefaultValue: true) as string ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }
}
