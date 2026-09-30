using System.Globalization;
using System.Text;
using System.Text.Json;
using Diagnyx.Core.Config;
using Diagnyx.Core.Logging;
using Diagnyx.Core.Retrieval;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

/// <summary>
/// diagnyx retrieve --question &lt;text&gt; [--since] [--until] [--source] [--limit]
///
/// Distinct from "diagnyx query": query is exact-filter, chronological
/// (oldest first) search; retrieve is relevance-ranked (best match first)
/// and built for a later "diagnyx ask" to feed to an LLM, not for
/// general-purpose log browsing. No LLM call happens here -- ranking is a
/// deterministic term-overlap score, so this command has no network
/// dependency and works with no LLM configured at all.
/// </summary>
internal static class RetrieveCommand
{
    private const int DefaultLimit = 20;

    internal static int Run(string[] args)
    {
        string? question = null, since = null, until = null, source = null;
        var limit = DefaultLimit;

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (flag is not ("--question" or "--since" or "--until" or "--source" or "--limit"))
                return Fail($"unknown option '{flag}'. Run 'diagnyx --help' for usage.");

            if (i + 1 >= args.Length || args[i + 1].Length == 0)
                return Fail($"{flag} requires a non-empty value.");

            var value = args[++i];
            switch (flag)
            {
                case "--question":
                    question = value;
                    break;
                case "--since":
                    if (!CliTimeParser.TryParse(value, out since))
                        return Fail(CliTimeParser.InvalidMessage(flag, value));
                    break;
                case "--until":
                    if (!CliTimeParser.TryParse(value, out until))
                        return Fail(CliTimeParser.InvalidMessage(flag, value));
                    break;
                case "--source":
                    source = value;
                    break;
                case "--limit":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1)
                        return Fail($"invalid --limit value '{value}'. Must be a positive integer.");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(question))
            return Fail("--question is required.");

        if (since is not null && until is not null && string.CompareOrdinal(since, until) > 0)
            return Fail("--since must not be later than --until.");

        var config = ConfigLoader.Load();
        var sink = SinkFactory.Create(config);
        if (sink is not IQueryableSink queryable)
            return Fail(
                $"the '{config.Sink.Type ?? "file"}' sink is write-only, so 'diagnyx retrieve' can't read from it. " +
                "Queryable sinks: file, sqlite, postgres, mysql, mssql.");

        var request = new RetrievalRequest(question, since, until, source, limit);
        foreach (var ranked in LogRetriever.Retrieve(queryable, request))
            Console.WriteLine(Serialize(ranked));

        return 0;
    }

    // Same seven canonical fields LogEntryJson.Serialize writes (reused via
    // WriteFields), plus one more: score. Kept flat, not nested, so existing
    // LogEntry-JSON tooling can still read those seven fields directly.
    private static string Serialize(RankedEntry ranked)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("score", ranked.Score);
            LogEntryJson.WriteFields(writer, ranked.Entry);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
