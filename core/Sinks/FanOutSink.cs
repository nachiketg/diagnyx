using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

/// <summary>
/// Writes an entry to every configured sink, in the order listed in
/// sink.types, continuing even if one fails -- a failure in one sink must
/// never stop the entry from reaching the others. Writes are sequential, not
/// concurrent: a slow sink (e.g. OTLP mid-retry) can delay the ones after
/// it, but never prevents them from being tried.
/// </summary>
internal sealed class FanOutSink(IReadOnlyList<string> types, IReadOnlyList<ISink> sinks) : ISink, IQueryableSink
{
    public int Write(LogEntry entry)
    {
        var anyFailed = false;

        for (var i = 0; i < sinks.Count; i++)
        {
            try
            {
                if (sinks[i].Write(entry) != 0)
                    anyFailed = true;
            }
            catch (Exception ex)
            {
                // ISink.Write must never throw (see docs/EXTENDING_SINKS.md),
                // but an isolation guarantee should not rely on every sink
                // honoring that -- one misbehaving sink still must not stop
                // the rest from getting the entry.
                Console.Error.WriteLine($"error: sink '{types[i]}' threw unexpectedly: {ex.Message}");
                anyFailed = true;
            }
        }

        // Non-zero tells the caller something needs attention, even though
        // every sink that could take the entry already has it -- isolation
        // is about the write still happening, not about hiding the failure.
        return anyFailed ? 1 : 0;
    }

    public IReadOnlyList<LogEntry> Query(LogQuery query)
    {
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is IQueryableSink queryable)
                return queryable.Query(query);
        }

        throw new InvalidOperationException(
            $"none of the configured sinks ({string.Join(", ", types)}) support 'diagnyx query'. " +
            "Queryable sinks: file, sqlite, postgres, mysql, mssql.");
    }
}
