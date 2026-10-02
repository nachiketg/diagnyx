using System.Text;
using System.Text.Json;

namespace Diagnyx.Core.Llm;

/// <summary>
/// Masks denylisted keys in a log entry's context JSON before it's sent to
/// an LLM -- the one place context data actually leaves the machine. Keys
/// are matched case-insensitively, at any depth (objects and arrays are
/// walked recursively), since a nested object can carry a secret just as
/// easily as a top-level field. The key itself is kept and its value
/// replaced with a fixed marker, rather than deleting the key outright, so
/// the model still sees that the field existed without seeing its value.
/// </summary>
internal static class ContextRedactor
{
    private const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Returns contextJson unchanged if there's nothing to redact (an empty
    /// denylist) or nothing to redact it from (null contextJson). If
    /// contextJson can't be parsed as JSON -- which shouldn't happen, since
    /// LogEntryJson validates it on write, but redaction is the one place
    /// where failing safe matters -- it's replaced with a placeholder
    /// rather than risking an unredacted value being sent as-is.
    /// </summary>
    public static string? Redact(string? contextJson, IReadOnlySet<string> denylist)
    {
        if (contextJson is null || denylist.Count == 0)
            return contextJson;

        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
                WriteRedacted(writer, doc.RootElement, denylist);

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            return "\"[unparseable context omitted]\"";
        }
    }

    private static void WriteRedacted(Utf8JsonWriter writer, JsonElement element, IReadOnlySet<string> denylist)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (denylist.Contains(property.Name))
                        writer.WriteStringValue(RedactedValue);
                    else
                        WriteRedacted(writer, property.Value, denylist);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteRedacted(writer, item, denylist);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
