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

    /// <summary>
    /// The inverse of <see cref="Serialize"/>. Returns false for anything that
    /// isn't a JSON object carrying the four required fields as strings, so a
    /// torn or foreign line in a log file can be skipped rather than fatal.
    /// </summary>
    public static bool TryParse(string line, out LogEntry entry)
    {
        entry = null!;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var timestamp = GetString(root, "timestamp");
            var level = GetString(root, "level");
            var message = GetString(root, "message");
            var source = GetString(root, "source");
            if (timestamp is null || level is null || message is null || source is null)
                return false;

            var context = root.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Object
                ? ctx.GetRawText()
                : null;

            entry = new LogEntry(
                timestamp, level, message, source, context,
                GetString(root, "traceId"), GetString(root, "spanId"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
