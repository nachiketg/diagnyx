using System.Globalization;
using Diagnyx.Core.Config;
using Diagnyx.Core.Logging;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

internal static class QueryCommand
{
    private const int DefaultLimit = 100;

    // ISO 8601 only: locale-style dates like 01/02/2026 are ambiguous.
    // K accepts "Z", "+hh:mm", or no zone (treated as UTC).
    private static readonly string[] AbsoluteFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    internal static int Run(string[] args)
    {
        string? since = null, until = null, level = null, source = null, contains = null;
        var limit = DefaultLimit;

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (flag is not ("--since" or "--until" or "--level" or "--source" or "--contains" or "--limit"))
                return Fail($"unknown option '{flag}'. Run 'diagnyx --help' for usage.");

            // Unlike 'log', a filter that silently fails to apply would return
            // unfiltered results, so a missing value is an error here.
            if (i + 1 >= args.Length || args[i + 1].Length == 0)
                return Fail($"{flag} requires a non-empty value.");

            var value = args[++i];
            switch (flag)
            {
                case "--since":
                    if (!TryParseTime(value, out since))
                        return Fail(InvalidTime(flag, value));
                    break;
                case "--until":
                    if (!TryParseTime(value, out until))
                        return Fail(InvalidTime(flag, value));
                    break;
                case "--level":
                    if (!LogLevel.IsValid(value))
                        return Fail($"Invalid level '{value}'. Valid values: debug, info, warn, error, fatal.");
                    level = value.ToLowerInvariant();
                    break;
                case "--source":
                    source = value;
                    break;
                case "--contains":
                    contains = value;
                    break;
                case "--limit":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1)
                        return Fail($"invalid --limit value '{value}'. Must be a positive integer.");
                    break;
            }
        }

        if (since is not null && until is not null && string.CompareOrdinal(since, until) > 0)
            return Fail("--since must not be later than --until.");

        var config = ConfigLoader.Load();
        var sink = SinkFactory.Create(config);
        if (sink is not IQueryableSink queryable)
            return Fail(
                $"the '{config.Sink.Type ?? "file"}' sink is write-only, so 'diagnyx query' can't read from it. " +
                "Queryable sinks: file, sqlite, postgres, mysql, mssql.");

        foreach (var entry in queryable.Query(new LogQuery(since, until, level, source, contains, limit)))
            Console.WriteLine(LogEntryJson.Serialize(entry));

        return 0;
    }

    // Accepts a relative duration ("30m", "2h", "7d" -- units s, m, h, d, w),
    // meaning that long before now, or an absolute ISO 8601 timestamp.
    private static bool TryParseTime(string value, out string? timestamp)
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

    private static string InvalidTime(string flag, string value) =>
        $"invalid {flag} value '{value}'. Use a relative duration like 30m, 2h or 7d " +
        "(units: s, m, h, d, w) or an ISO 8601 timestamp like 2026-09-21T10:00:00Z.";

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
