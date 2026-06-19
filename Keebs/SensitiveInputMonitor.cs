using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace Keebs;

internal sealed class SensitiveInputMonitor : IDisposable
{
    private static readonly string[] SensitiveKeywords =
    [
        "password", "passcode", "security code", "secret", "one-time", "one time",
        "verification code", "recovery code", "social security"
    ];

    private static readonly string[] SensitiveShortTokens =
    [
        "pin", "cvv", "cvc", "otp", "2fa", "mfa", "ssn"
    ];

    private static readonly string[] SensitiveTokenPhrases =
    [
        "access token", "api token", "auth token", "bearer token", "personal access token",
        "refresh token", "secret token"
    ];

    private static readonly string[] SensitivePromptKeywords =
    [
        "password", "passphrase", "passcode", "pin", "otp", "verification code",
        "recovery code", "security code"
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
               (SensitiveKeywords.Any(keyword => className.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                SensitiveShortTokens.Any(token => ContainsToken(className, token)));
    }

    internal static bool IsSensitiveMetadata(string metadata)
    {
        return SensitiveKeywords.Any(keyword =>
                   metadata.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               SensitiveShortTokens.Any(token => ContainsToken(metadata, token)) ||
               SensitiveTokenPhrases.Any(phrase =>
                   metadata.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsSensitiveTextContext(string textBeforeCaret)
    {
        if (string.IsNullOrWhiteSpace(textBeforeCaret))
        {
            return false;
        }

        var context = textBeforeCaret.Replace('\r', '\n');
        var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lastLine = lines.LastOrDefault() ?? context.Trim();
        if (lastLine.Length == 0)
        {
            return false;
        }

        if (lastLine.Length > 240)
        {
            lastLine = lastLine[^240..];
        }

        var promptLike = lastLine.EndsWith(":", StringComparison.Ordinal) ||
                         lastLine.Contains("password for", StringComparison.OrdinalIgnoreCase) ||
                         lastLine.Contains("passphrase for", StringComparison.OrdinalIgnoreCase);

        return promptLike &&
               SensitivePromptKeywords.Any(keyword => lastLine.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var index = 0;
        while (index < text.Length)
        {
            var matchIndex = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                return false;
            }

            var before = matchIndex == 0 ? '\0' : text[matchIndex - 1];
            var afterIndex = matchIndex + token.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
            if (!IsTokenCharacter(before) && !IsTokenCharacter(after))
            {
                return true;
            }

            index = matchIndex + token.Length;
        }

        return false;
    }

    private static bool IsTokenCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    internal static bool IsCredentialPromptCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var command = commandLine.Trim();
        if (command.Length == 0 || command.StartsWith('#'))
        {
            return false;
        }

        if (command.StartsWith("sudo ", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("sudo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var executable = GetCommandExecutable(command);
        return executable.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
               executable.Equals("ssh.exe", StringComparison.OrdinalIgnoreCase) ||
               executable.Equals("scp", StringComparison.OrdinalIgnoreCase) ||
               executable.Equals("scp.exe", StringComparison.OrdinalIgnoreCase) ||
               executable.Equals("sftp", StringComparison.OrdinalIgnoreCase) ||
               executable.Equals("sftp.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCommandExecutable(string command)
    {
        var start = 0;
        while (start < command.Length && char.IsWhiteSpace(command[start]))
        {
            start++;
        }

        if (start >= command.Length)
        {
            return string.Empty;
        }

        var end = start;
        while (end < command.Length && !char.IsWhiteSpace(command[end]))
        {
            end++;
        }

        var executable = command[start..end];
        var slashIndex = executable.LastIndexOfAny(['/', '\\']);
        return slashIndex >= 0 ? executable[(slashIndex + 1)..] : executable;
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
