namespace Keebs;

internal static class TypingRunMetrics
{
    public static TypingRun CreateRun(string prompt, string typedText, TimeSpan elapsed, DateTimeOffset timestamp)
    {
        var elapsedSeconds = Math.Max(0, elapsed.TotalSeconds);
        var elapsedMinutes = elapsedSeconds / 60;
        var wordsPerMinute = elapsedMinutes <= 0
            ? 0
            : (typedText.Length / 5.0) / elapsedMinutes;
        var editDistance = GetLevenshteinDistance(prompt, typedText);
        var accuracy = prompt.Length == 0
            ? typedText.Length == 0 ? 1 : 0
            : Math.Max(0, (prompt.Length - editDistance) / (double)prompt.Length);

        return new TypingRun(
            timestamp,
            prompt,
            typedText,
            elapsedSeconds,
            wordsPerMinute,
            accuracy,
            editDistance);
    }

    private static int GetLevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
