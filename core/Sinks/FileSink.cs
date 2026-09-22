using System.Text;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal sealed class FileSink(string path, FileRotationPolicy? rotation = null) : ISink, IQueryableSink
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public int Write(LogEntry entry)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            TryRotate();

            File.AppendAllText(path, LogEntryJson.Serialize(entry) + "\n", Utf8NoBom);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to write log entry: {ex.Message}");
            return 1;
        }
    }

    // A rotation failure (e.g. a rotated file locked by another process) is
    // reported but never blocks the write -- an oversized or stale-but-intact
    // file beats losing the entry.
    private void TryRotate()
    {
        if (rotation is null || !rotation.ShouldRotate(path))
            return;

        try
        {
            rotation.Rotate(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: log rotation failed, continuing to write to the existing file: {ex.Message}");
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
