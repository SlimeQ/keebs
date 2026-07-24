using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Text;

namespace Keebs;

internal static class FocusedTextContextReader
{
    private const int MaxContextCharacters = 500;
    private const string ArtifactToken = "xhtml";

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

    public static FocusedTextContext ReadFocusedTextContext()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return element is null ? FocusedTextContext.Unavailable : ReadFocusedTextContext(element);
        }
        catch (ElementNotAvailableException)
        {
            return FocusedTextContext.Unavailable;
        }
        catch (InvalidOperationException)
        {
            return FocusedTextContext.Unavailable;
        }
    }

    internal static FocusedTextContext ReadFocusedTextContext(AutomationElement element)
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
        var providerFound = false;
        foreach (var candidate in candidates)
        {
            if (TryGetValuePatternContext(candidate, out var textBeforeCaret, ref providerFound))
            {
                return FocusedTextContext.FromSanitizedText(textBeforeCaret);
            }
        }

        foreach (var candidate in candidates)
        {
            if (TryGetTextPatternContext(candidate, out var textBeforeCaret, ref providerFound))
            {
                return FocusedTextContext.FromSanitizedText(textBeforeCaret);
            }
        }

        // Nothing produced text. An element that answered at all reports an empty
        // field; an element that never answered reports nothing at all. A control
        // whose value is empty because its text lives behind another pattern is
        // covered by the earlier passes, so it does not reach this as "empty".
        return providerFound ? FocusedTextContext.Empty : FocusedTextContext.Unavailable;
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

    private static bool TryGetTextPatternContext(
        AutomationElement element,
        out string textBeforeCaret,
        ref bool providerFound)
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
                // Without a caret there is no way to tell an empty field from an
                // unreadable one, so leave the provider unconfirmed.
                return false;
            }

            var selectionStart = selection[0].Clone();
            selectionStart.MoveEndpointByRange(TextPatternRangeEndpoint.End, selectionStart, TextPatternRangeEndpoint.Start);

            var beforeCaret = textPattern.DocumentRange.Clone();
            beforeCaret.MoveEndpointByRange(TextPatternRangeEndpoint.End, selectionStart, TextPatternRangeEndpoint.Start);
            providerFound = true;
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

    private static bool TryGetValuePatternContext(
        AutomationElement element,
        out string textBeforeCaret,
        ref bool providerFound)
    {
        textBeforeCaret = string.Empty;

        if (!TryReadValue(element, out var value))
        {
            return false;
        }

        providerFound = true;
        return TryTrimContext(value, out textBeforeCaret);
    }

    private static bool TryReadValue(AutomationElement element, out string value)
    {
        value = string.Empty;

        if (!TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            return false;
        }

        try
        {
            value = valuePattern.Current.Value ?? string.Empty;
            return true;
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
        var pendingBoundary = false;

        foreach (var character in text)
        {
            // Joiners sit inside a word, so dropping them keeps the word whole.
            if (character is '\u200C' or '\u200D')
            {
                continue;
            }

            // A zero-width space, byte-order mark, embedded object, or stray
            // control character separates two runs of text. Applications use them
            // to butt hidden text against the editable value, so they have to read
            // as a word boundary: deleting them would merge the hidden run into
            // the word being typed, while a boundary keeps that run as context.
            if (character is '\u200B' or '\uFEFF' or '\uFFFC' ||
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                pendingBoundary = true;
                continue;
            }

            AppendPendingBoundary(builder, ref pendingBoundary, character);
            builder.Append(character);
        }

        AppendPendingBoundary(builder, ref pendingBoundary, null);

        var sanitized = RemoveArtifactTokens(builder.ToString().TrimStart()).TrimStart();

        return sanitized.Trim().Equals("html", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : sanitized;
    }

    private static void AppendPendingBoundary(StringBuilder builder, ref bool pendingBoundary, char? nextCharacter)
    {
        if (!pendingBoundary)
        {
            return;
        }

        pendingBoundary = false;

        // Whitespace on either side already separates the two runs, so adding a
        // space would only distort how many boundary characters the field holds.
        if (builder.Length == 0 ||
            char.IsWhiteSpace(builder[^1]) ||
            nextCharacter is { } next && char.IsWhiteSpace(next))
        {
            return;
        }

        builder.Append(' ');
    }

    // Firefox exposes a stray "xhtml" namespace token in its accessibility text.
    // It leads the document text, but it also turns up glued to the front of an
    // inline run, so it has to be removed wherever it starts a word instead of
    // only at the very front of the context. A standalone "xhtml" that follows
    // real text is left alone because it is more likely to be typed content.
    private static string RemoveArtifactTokens(string text)
    {
        if (text.Length < ArtifactToken.Length)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var leading = true;

        for (var index = 0; index < text.Length;)
        {
            if (IsArtifactTokenAt(text, index, builder, leading))
            {
                index += ArtifactToken.Length;
                continue;
            }

            leading &= char.IsWhiteSpace(text[index]);
            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    private static bool IsArtifactTokenAt(string text, int index, StringBuilder emitted, bool leading)
    {
        if (!text.AsSpan(index).StartsWith(ArtifactToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (emitted.Length > 0 && IsWordCharacter(emitted[^1]))
        {
            return false;
        }

        var tokenEnd = index + ArtifactToken.Length;
        var glued = tokenEnd < text.Length && IsWordCharacter(text[tokenEnd]);
        return glued || leading;
    }

    private static bool IsWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '\'';
    }
}

internal readonly record struct FocusedTextContext(bool IsAvailable, string TextBeforeCaret)
{
    public static FocusedTextContext Unavailable => new(false, string.Empty);

    public static FocusedTextContext Empty => new(true, string.Empty);

    public static FocusedTextContext FromSanitizedText(string text)
    {
        return new FocusedTextContext(true, text);
    }
}
