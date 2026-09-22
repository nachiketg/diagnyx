using System.Globalization;

namespace Diagnyx.Core.Logging;

/// <summary>
/// Parses the duration syntax "diagnyx query"'s --since/--until and the file
/// sink's retention config (sink.file.maxAge) share: a whole number plus a
/// unit -- s, m, h, d, w (seconds, minutes, hours, days, weeks).
/// </summary>
internal static class DurationParser
{
    public static bool TryParse(string value, out TimeSpan span)
    {
        span = default;

        if (value.Length < 2)
            return false;

        var unit = value[^1];
        if (!"smhdw".Contains(unit))
            return false;

        if (!int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return false;

        try
        {
            span = unit switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                _   => TimeSpan.FromDays(7.0 * amount),
            };
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return false; // Amount too large for a TimeSpan to represent.
        }
    }
}
