using System.Text;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal sealed class FileSink(string path) : ISink
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
}
