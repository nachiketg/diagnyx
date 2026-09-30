using System.Globalization;
using Diagnyx.Core.Config;
using Diagnyx.Core.Logging;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

internal static class QueryCommand
{
    private const int DefaultLimit = 100;

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
                    if (!CliTimeParser.TryParse(value, out since))
                        return Fail(CliTimeParser.InvalidMessage(flag, value));
                    break;
                case "--until":
                    if (!CliTimeParser.TryParse(value, out until))
                        return Fail(CliTimeParser.InvalidMessage(flag, value));
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

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
