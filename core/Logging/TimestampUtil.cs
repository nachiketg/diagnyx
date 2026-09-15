using System.Globalization;

namespace Diagnyx.Core.Logging;

internal static class TimestampUtil
{
    /// <summary>
    /// Parses a LogEntry.Timestamp (docs/SCHEMA.md's fixed
    /// "yyyy-MM-ddTHH:mm:ss.fffZ" format) into nanoseconds since the Unix
    /// epoch, for wire formats that require that unit (OTLP, Loki).
    /// </summary>
    public static long ToUnixNanoseconds(string isoTimestamp)
    {
        var offset = DateTimeOffset.ParseExact(
            isoTimestamp, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return offset.ToUnixTimeMilliseconds() * 1_000_000L;
    }
}
