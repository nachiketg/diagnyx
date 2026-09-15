using System.Text;
using System.Text.Json;

namespace Diagnyx.Core.Logging;

/// <summary>
/// Canonical JSON Lines serialization of a LogEntry, per docs/SCHEMA.md.
/// Shared by every sink that writes or forwards the raw log line (file,
/// Loki) rather than a structured/columnar representation of it.
/// </summary>
internal static class LogEntryJson
{
    public static string Serialize(LogEntry entry)
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
