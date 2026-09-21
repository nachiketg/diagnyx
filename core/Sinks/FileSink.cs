using System.Text;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal sealed class FileSink(string path) : ISink, IQueryableSink
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public int Write(LogEntry entry)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(path, LogEntryJson.Serialize(entry) + "\n", Utf8NoBom);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to write log entry: {ex.Message}");
            return 1;
        }
    }

    public IReadOnlyList<LogEntry> Query(LogQuery query)
    {
        if (!File.Exists(path))
            return [];

        // FileShare.ReadWrite: the log is usually still being appended to by a
        // live app, and a stricter share mode would fail on Windows.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Utf8NoBom);

        // The file is append-only, so it is already chronological. Keep only a
        // sliding window of the newest matches rather than every match.
        var window = new Queue<LogEntry>();
        var unreadable = 0;

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!LogEntryJson.TryParse(line, out var entry))
            {
                unreadable++;
                continue;
            }

            if (!query.Matches(entry))
                continue;

            if (window.Count == query.Limit)
                window.Dequeue();
            window.Enqueue(entry);
        }

        if (unreadable > 0)
            Console.Error.WriteLine($"warning: skipped {unreadable} unreadable line(s) in {path}");

        return window.ToArray();
    }
}
