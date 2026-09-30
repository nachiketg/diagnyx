using System.Globalization;

namespace Diagnyx.Core.Logging;

/// <summary>
/// Parses the --since/--until style CLI flags "diagnyx query" and
/// "diagnyx retrieve" share: a relative duration (DurationParser) meaning
/// that long before now, or an absolute ISO 8601 timestamp.
/// </summary>
internal static class CliTimeParser
{
    // ISO 8601 only: locale-style dates like 01/02/2026 are ambiguous.
    // K accepts "Z", "+hh:mm", or no zone (treated as UTC).
    private static readonly string[] AbsoluteFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    public static bool TryParse(string value, out string? timestamp)
    {
        timestamp = null;

        if (DurationParser.TryParse(value, out var span))
        {
            try
            {
                timestamp = TimestampUtil.ToTimestamp(DateTimeOffset.UtcNow - span);
                return true;
            }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            {
                return false; // Duration reaches before year 1.
            }
        }

        if (DateTimeOffset.TryParseExact(
                value, AbsoluteFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var absolute))
        {
            timestamp = TimestampUtil.ToTimestamp(absolute);
            return true;
        }

        return false;
    }

    public static string InvalidMessage(string flag, string value) =>
        $"invalid {flag} value '{value}'. Use a relative duration like 30m, 2h or 7d " +
        "(units: s, m, h, d, w) or an ISO 8601 timestamp like 2026-09-21T10:00:00Z.";
}
