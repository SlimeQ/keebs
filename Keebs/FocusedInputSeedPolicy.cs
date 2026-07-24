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
    public static FocusedInputSeed Resolve(FocusedTextContext context, FocusedInputSeedKind kind, string currentWord)
    {
        if (!context.IsAvailable)
        {
            // Nothing readable: only a focus change is worth discarding context over.
            return kind == FocusedInputSeedKind.FocusChange
                ? new FocusedInputSeed(true, string.Empty)
                : FocusedInputSeed.Skip;
        }

        // A backspace can only shrink the word at the caret. A read that still
        // shows a longer word ran before the application applied the delete, and
        // seeding from it would put the deleted character back.
        if (kind == FocusedInputSeedKind.Backspace &&
            TextSession.ParseCurrentWord(context.TextBeforeCaret).Length > currentWord.Length)
        {
            return FocusedInputSeed.Skip;
        }

        return new FocusedInputSeed(true, context.TextBeforeCaret);
    }
}
