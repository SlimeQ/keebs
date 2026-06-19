using System.IO;
using System.Text.Json;

namespace Keebs;

internal sealed class TypingRunRecorder
{
    private readonly string _runsPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public TypingRunRecorder()
        : this(GetDefaultRunsPath())
    {
    }

    internal TypingRunRecorder(string runsPath)
    {
        _runsPath = runsPath;
    }

    public string RunsPath => _runsPath;

    public void Append(TypingRun run)
    {
        var directory = Path.GetDirectoryName(_runsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(run, _jsonOptions);
        File.AppendAllText(_runsPath, $"{json}{Environment.NewLine}");
    }

    private static string GetDefaultRunsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Keebs",
            "typing-runs.jsonl");
    }
}
