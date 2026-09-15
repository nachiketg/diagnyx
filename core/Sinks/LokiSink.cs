using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

/// <summary>
/// Pushes log entries to a Grafana Loki-compatible endpoint via its HTTP
/// push API (POST /loki/api/v1/push).
///
/// Only "source" and "level" become Loki labels -- Loki indexes streams by
/// label, and label values must stay low-cardinality (unlike per-entry
/// values such as message or traceId, which would create a new stream per
/// entry and degrade query performance). The log line itself is the full
/// canonical JSON entry from docs/SCHEMA.md, so every field -- including
/// traceId/spanId and context -- stays queryable via LogQL's `| json`
/// parser in Grafana Explore.
/// </summary>
internal sealed class LokiSink(string endpoint) : ISink
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _pushUrl = BuildPushUrl(endpoint);

    public int Write(LogEntry entry)
    {
        string payload;
        try
        {
            payload = BuildPayload(entry);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: Loki export failed: {ex.Message}");
            return 1;
        }

        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Post, _pushUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            using var response = client.Send(request);

            // Loki returns 204 No Content on a successful push.
            if (response.IsSuccessStatusCode)
                return 0;

            var body = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
            Console.Error.WriteLine(
                $"error: Loki export failed: {(int)response.StatusCode} {response.ReasonPhrase}" +
                (string.IsNullOrWhiteSpace(body) ? "" : $" -- {body}"));
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: Loki export failed: {ex.Message}");
            return 1;
        }
    }

    private static string BuildPushUrl(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/loki/api/v1/push", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/loki/api/v1/push";
    }

    private static string BuildPayload(LogEntry entry)
    {
        var line = LogEntryJson.Serialize(entry);
        var timestampNs = TimestampUtil.ToUnixNanoseconds(entry.Timestamp).ToString(CultureInfo.InvariantCulture);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("streams");
            writer.WriteStartArray();
            writer.WriteStartObject();

            writer.WritePropertyName("stream");
            writer.WriteStartObject();
            writer.WriteString("service_name", entry.Source);
            writer.WriteString("level", entry.Level);
            writer.WriteEndObject();

            writer.WritePropertyName("values");
            writer.WriteStartArray();
            writer.WriteStartArray();
            writer.WriteStringValue(timestampNs);
            writer.WriteStringValue(line);
            writer.WriteEndArray();
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
