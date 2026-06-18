using System.Windows.Automation;

namespace Keebs;

internal sealed class SensitiveInputMonitor : IDisposable
{
    private static readonly string[] SensitiveKeywords =
    [
        "password", "passcode", "pin", "cvv", "cvc", "security code", "secret",
        "otp", "one-time", "one time", "2fa", "mfa", "verification code",
        "recovery code", "token", "ssn", "social security"
    ];

    private readonly AutomationFocusChangedEventHandler _focusChangedHandler;
    private bool _started;

    public SensitiveInputMonitor()
    {
        _focusChangedHandler = (_, _) => UpdateFromFocusedElement();
    }

    public event EventHandler? StateChanged;

    public bool IsSensitive { get; private set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Automation.AddAutomationFocusChangedEventHandler(_focusChangedHandler);
        UpdateFromFocusedElement();
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

    private void UpdateFromFocusedElement()
    {
        var sensitive = IsFocusedElementSensitive();
        if (sensitive == IsSensitive)
        {
            return;
        }

        IsSensitive = sensitive;
        StateChanged?.Invoke(this, EventArgs.Empty);
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

        var metadata = string.Join(" ",
            GetStringProperty(element, AutomationElement.NameProperty),
            GetStringProperty(element, AutomationElement.AutomationIdProperty),
            GetStringProperty(element, AutomationElement.ClassNameProperty),
            GetStringProperty(element, AutomationElement.HelpTextProperty));

        return SensitiveKeywords.Any(keyword =>
            metadata.Contains(keyword, StringComparison.OrdinalIgnoreCase));
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
