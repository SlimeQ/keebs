using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Keebs;

internal static class FocusedTextContextReader
{
    private const int MaxContextCharacters = 500;

    public static string GetTextBeforeCaret()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return element is null ? string.Empty : GetTextBeforeCaret(element);
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    internal static string GetTextBeforeCaret(AutomationElement element)
    {
        for (var depth = 0; element is not null && depth < 6; depth++)
        {
            if (TryGetValuePatternContext(element, out var textBeforeCaret))
            {
                return textBeforeCaret;
            }

            if (TryGetTextPatternContext(element, out textBeforeCaret))
            {
                return textBeforeCaret;
            }

            try
            {
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
        }

        return string.Empty;
    }

    private static bool TryGetTextPatternContext(AutomationElement element, out string textBeforeCaret)
    {
        textBeforeCaret = string.Empty;

        if (!TryGetPattern<TextPattern>(element, TextPattern.Pattern, out var textPattern))
        {
            return false;
        }

        try
        {
            var selection = textPattern.GetSelection();
            if (selection.Length == 0)
            {
                return false;
            }

            var selectionStart = selection[0].Clone();
            selectionStart.MoveEndpointByRange(TextPatternRangeEndpoint.End, selectionStart, TextPatternRangeEndpoint.Start);

            var beforeCaret = textPattern.DocumentRange.Clone();
            beforeCaret.MoveEndpointByRange(TextPatternRangeEndpoint.End, selectionStart, TextPatternRangeEndpoint.Start);
            return TryTrimContext(beforeCaret.GetText(MaxContextCharacters), out textBeforeCaret);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetValuePatternContext(AutomationElement element, out string textBeforeCaret)
    {
        textBeforeCaret = string.Empty;

        if (!TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            return false;
        }

        try
        {
            return TryTrimContext(valuePattern.Current.Value, out textBeforeCaret);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetPattern<TPattern>(
        AutomationElement element,
        AutomationPattern pattern,
        out TPattern typedPattern)
        where TPattern : class
    {
        typedPattern = null!;

        if (!element.TryGetCurrentPattern(pattern, out var rawPattern) || rawPattern is not TPattern patternValue)
        {
            return false;
        }

        typedPattern = patternValue;
        return true;
    }

    private static bool TryTrimContext(string? text, out string trimmedText)
    {
        trimmedText = string.IsNullOrEmpty(text)
            ? string.Empty
            : text[^Math.Min(text.Length, MaxContextCharacters)..];

        return trimmedText.Length > 0 && IsUsableSeedText(trimmedText);
    }

    internal static bool IsUsableSeedText(string text)
    {
        var trimmedText = text.Trim();
        if (trimmedText.Length == 0)
        {
            return false;
        }

        return !trimmedText.Equals("xhtml", StringComparison.OrdinalIgnoreCase) &&
               !trimmedText.Equals("html", StringComparison.OrdinalIgnoreCase);
    }
}
