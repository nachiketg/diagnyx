using System.Globalization;

namespace Diagnyx.Core.Logging;

internal static class TimestampUtil
{
    /// <summary>
    /// The fixed-width UTC format every sink stores (docs/SCHEMA.md). Being
    /// fixed-width, timestamps in this format sort chronologically as plain
    /// strings, which "diagnyx query" relies on for time filtering.
    /// </summary>
    public const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string ToTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a LogEntry.Timestamp into nanoseconds since the Unix epoch,
    /// for wire formats that require that unit (OTLP, Loki).
    /// </summary>
    public static long ToUnixNanoseconds(string isoTimestamp)
    {
        var offset = DateTimeOffset.ParseExact(
            isoTimestamp, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return offset.ToUnixTimeMilliseconds() * 1_000_000L;
    }
}
