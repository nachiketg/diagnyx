using System.Text;
using System.Text.Json;
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

            File.AppendAllText(path, Serialize(entry) + "\n", Utf8NoBom);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to write log entry: {ex.Message}");
            return 1;
        }
    }

    private static string Serialize(LogEntry entry)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteString("timestamp", entry.Timestamp);
        writer.WriteString("level", entry.Level);
        writer.WriteString("message", entry.Message);
        writer.WriteString("source", entry.Source);

        writer.WritePropertyName("context");
        if (entry.ContextJson is null)
            writer.WriteNullValue();
        else
            writer.WriteRawValue(entry.ContextJson, skipInputValidation: true);

        if (entry.TraceId is null)
            writer.WriteNull("traceId");
        else
            writer.WriteString("traceId", entry.TraceId);

        if (entry.SpanId is null)
            writer.WriteNull("spanId");
        else
            writer.WriteString("spanId", entry.SpanId);

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
