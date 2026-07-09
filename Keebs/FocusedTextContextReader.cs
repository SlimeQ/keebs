using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Text;

namespace Keebs;

internal static class FocusedTextContextReader
{
    private const int MaxContextCharacters = 500;

    public static bool IsFocusedElementTextInput()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return element is not null && IsTextInputElement(element);
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
        var candidates = new List<AutomationElement>();
        for (var depth = 0; element is not null && depth < 6; depth++)
        {
            candidates.Add(element);

            try
            {
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            catch (ElementNotAvailableException)
            {
                break;
            }
        }

        // A browser can expose a document-like TextPattern on an inner element and
        // the actual editable ValuePattern on an ancestor. Prefer the editable value
        // before falling back to document text so UI metadata is not treated as input.
        foreach (var candidate in candidates)
        {
            if (TryGetValuePatternContext(candidate, out var textBeforeCaret))
            {
                return textBeforeCaret;
            }
        }

        foreach (var candidate in candidates)
        {
            if (TryGetTextPatternContext(candidate, out var textBeforeCaret))
            {
                return textBeforeCaret;
            }
        }

        return string.Empty;
    }

    internal static bool IsTextInputElement(AutomationElement element)
    {
        for (var depth = 0; element is not null && depth < 6; depth++)
        {
            if (TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out _) ||
                TryGetPattern<TextPattern>(element, TextPattern.Pattern, out _))
            {
                return true;
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

        return false;
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
        var context = string.IsNullOrEmpty(text)
            ? string.Empty
            : text[^Math.Min(text.Length, MaxContextCharacters)..];
        trimmedText = SanitizeSeedText(context);

        return trimmedText.Trim().Length > 0;
    }

    internal static bool IsUsableSeedText(string text)
    {
        return SanitizeSeedText(text).Trim().Length > 0;
    }

    internal static string SanitizeSeedText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
            {
                continue;
            }

            if (character == '\uFFFC' || char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(character);
        }

        var sanitized = builder.ToString().TrimStart();
        while (sanitized.StartsWith("xhtml", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = sanitized["xhtml".Length..].TrimStart();
        }

        return sanitized.Trim().Equals("html", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : sanitized;
    }
}
