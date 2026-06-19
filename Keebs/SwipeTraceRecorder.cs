using System.IO;
using System.Text.Json;

namespace Keebs;

internal sealed class SwipeTraceRecorder
{
    private readonly string _eventsPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public SwipeTraceRecorder()
        : this(GetDefaultEventsPath())
    {
    }

    internal SwipeTraceRecorder(string eventsPath)
    {
        _eventsPath = eventsPath;
    }

    public string EventsPath => _eventsPath;

    public void Append(SwipeTraceEvent traceEvent)
    {
        try
        {
            var directory = Path.GetDirectoryName(_eventsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(traceEvent, _jsonOptions);
            File.AppendAllText(_eventsPath, $"{json}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetDefaultEventsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Keebs",
            "swipe-traces.jsonl");
    }
}
