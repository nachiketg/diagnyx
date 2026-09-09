using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

/// <summary>
/// Exports log entries to any OTel-compatible collector via OTLP/HTTP with
/// JSON encoding, using the field mapping documented in docs/OTEL_MAPPING.md.
/// </summary>
internal sealed class OtlpSink(string endpoint) : ISink
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly string ScopeVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly Dictionary<string, (int Number, string Text)> Severity = new()
    {
        ["debug"] = (5, "DEBUG"),
        ["info"] = (9, "INFO"),
        ["warn"] = (13, "WARN"),
        ["error"] = (17, "ERROR"),
        ["fatal"] = (21, "FATAL"),
    };

    private readonly string _logsUrl = BuildLogsUrl(endpoint);

    public int Write(LogEntry entry)
    {
        try
        {
            var payload = BuildPayload(entry);

            using var client = new HttpClient { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, _logsUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            using var response = client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
                Console.Error.WriteLine(
                    $"error: OTLP export failed: {(int)response.StatusCode} {response.ReasonPhrase}" +
                    (string.IsNullOrWhiteSpace(body) ? "" : $" -- {body}"));
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: OTLP export failed: {ex.Message}");
            return 1;
        }
    }

    private static string BuildLogsUrl(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/v1/logs", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/v1/logs";
    }

    private static string BuildPayload(LogEntry entry)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            var (severityNumber, severityText) = Severity[entry.Level];
            var timeUnixNano = ToUnixNano(entry.Timestamp);

            writer.WriteStartObject();
            writer.WritePropertyName("resourceLogs");
            writer.WriteStartArray();
            writer.WriteStartObject();

            writer.WritePropertyName("resource");
            writer.WriteStartObject();
            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            WriteKeyValue(writer, "service.name", w => w.WriteString("stringValue", entry.Source));
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WritePropertyName("scopeLogs");
            writer.WriteStartArray();
            writer.WriteStartObject();

            writer.WritePropertyName("scope");
            writer.WriteStartObject();
            writer.WriteString("name", "diagnyx");
            writer.WriteString("version", ScopeVersion);
            writer.WriteEndObject();

            writer.WritePropertyName("logRecords");
            writer.WriteStartArray();
            writer.WriteStartObject();

            // Diagnyx doesn't distinguish event time from ingestion time, so
            // both OTel timestamps get the same value.
            writer.WriteString("timeUnixNano", timeUnixNano);
            writer.WriteString("observedTimeUnixNano", timeUnixNano);
            writer.WriteNumber("severityNumber", severityNumber);
            writer.WriteString("severityText", severityText);

            writer.WritePropertyName("body");
            writer.WriteStartObject();
            writer.WriteString("stringValue", entry.Message);
            writer.WriteEndObject();

            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            if (entry.ContextJson is not null)
            {
                using var context = JsonDocument.Parse(entry.ContextJson);
                foreach (var property in context.RootElement.EnumerateObject())
                    WriteKeyValue(writer, property.Name, w => WriteAnyValue(w, property.Value));
            }
            writer.WriteEndArray();

            writer.WriteNumber("droppedAttributesCount", 0);
            // Diagnyx does not currently retain the W3C traceparent flags
            // byte (see core/Logging/TraceContext.cs) -- defaults to FLAG_NONE.
            writer.WriteNumber("flags", 0);

            if (entry.TraceId is not null)
                writer.WriteString("traceId", entry.TraceId);
            if (entry.SpanId is not null)
                writer.WriteString("spanId", entry.SpanId);

            writer.WriteEndObject(); // logRecord
            writer.WriteEndArray();  // logRecords
            writer.WriteEndObject(); // scopeLogs[0]
            writer.WriteEndArray();  // scopeLogs
            writer.WriteEndObject(); // resourceLogs[0]
            writer.WriteEndArray();  // resourceLogs
            writer.WriteEndObject(); // root
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteKeyValue(Utf8JsonWriter writer, string key, Action<Utf8JsonWriter> writeValue)
    {
        writer.WriteStartObject();
        writer.WriteString("key", key);
        writer.WritePropertyName("value");
        writer.WriteStartObject();
        writeValue(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    // Converts one JSON value into an OTLP AnyValue, matching the JSON type
    // table in docs/OTEL_MAPPING.md.
    private static void WriteAnyValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                writer.WriteString("stringValue", value.GetString());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBoolean("boolValue", value.GetBoolean());
                break;
            case JsonValueKind.Number when value.TryGetInt64(out var i):
                writer.WriteString("intValue", i.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Number:
                writer.WriteNumber("doubleValue", value.GetDouble());
                break;
            case JsonValueKind.Array:
                writer.WritePropertyName("arrayValue");
                writer.WriteStartObject();
                writer.WritePropertyName("values");
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    writer.WriteStartObject();
                    WriteAnyValue(writer, item);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case JsonValueKind.Object:
                writer.WritePropertyName("kvlistValue");
                writer.WriteStartObject();
                writer.WritePropertyName("values");
                writer.WriteStartArray();
                foreach (var property in value.EnumerateObject())
                    WriteKeyValue(writer, property.Name, w => WriteAnyValue(w, property.Value));
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            default:
                writer.WriteString("stringValue", "");
                break;
        }
    }

    private static string ToUnixNano(string isoTimestamp)
    {
        var offset = DateTimeOffset.ParseExact(
            isoTimestamp, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return (offset.ToUnixTimeMilliseconds() * 1_000_000L).ToString(CultureInfo.InvariantCulture);
    }
}
