namespace Keebs;

internal enum FocusedInputSeedKind
{
    /// <summary>A new field took focus, so any previous context is void.</summary>
    FocusChange,

    /// <summary>The caret moved or a selection was replaced.</summary>
    CaretMove,

    /// <summary>Text was deleted behind the caret.</summary>
    Backspace
}

internal readonly record struct FocusedInputSeed(bool ShouldApply, string TextBeforeCaret)
{
    public static FocusedInputSeed Skip => new(false, string.Empty);
}

/// <summary>
/// Decides whether an accessibility read is a better description of the focused
/// field than the locally tracked session. A read races the application it is
/// reading, so it is only trusted when it cannot be explained as stale.
/// </summary>
internal static class FocusedInputSeedPolicy
{
    public static FocusedInputSeed Resolve(
        FocusedTextContext context,
        FocusedInputSeedKind kind,
        PredictionContext session)
    {
        if (!context.IsAvailable)
        {
            // Nothing readable: only a focus change is worth discarding context over.
            return kind == FocusedInputSeedKind.FocusChange
                ? new FocusedInputSeed(true, string.Empty)
                : FocusedInputSeed.Skip;
        }

        if (kind == FocusedInputSeedKind.Backspace && !DescribesSameText(context.TextBeforeCaret, session))
        {
            return FocusedInputSeed.Skip;
        }

        return new FocusedInputSeed(true, context.TextBeforeCaret);
    }

    /// <summary>
    /// Whether a read taken after a backspace plausibly describes the text the
    /// session was already tracking. A backspace deletes one character behind the
    /// caret, so anything the read cannot explain that way came from a provider
    /// that is out of step with the field being typed into.
    /// </summary>
    private static bool DescribesSameText(string textBeforeCaret, PredictionContext session)
    {
        var sessionHasContext = session.CurrentWord.Length > 0 || session.PreviousWords.Count > 0;

        // Providers answer with nothing for plenty of reasons that are not "the
        // user deleted everything" -- a control whose text lives behind a pattern
        // it did not offer, a document that has not settled, a value that is not
        // the editable text. Believing them would throw away real context.
        if (textBeforeCaret.Trim().Length == 0)
        {
            return !sessionHasContext;
        }

        var readWord = TextSession.ParseCurrentWord(textBeforeCaret);
        if (readWord.Length == 0)
        {
            return session.CurrentWord.Length == 0;
        }

        // The word at the caret can only have got shorter. A longer one means the
        // read ran before the delete landed; one that is not the tail of the word
        // being tracked belongs to different text entirely. Matching the tail is
        // what lets a word carrying hidden metadata be repaired.
        return session.CurrentWord.EndsWith(readWord, StringComparison.Ordinal);
    }
}
