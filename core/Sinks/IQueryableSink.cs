using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

/// <summary>
/// Implemented by sinks whose storage "diagnyx query" can read back (file and
/// the RDBMS sinks). Export-only sinks (otlp, loki) deliberately don't.
/// </summary>
internal interface IQueryableSink
{
    /// <summary>
    /// Returns the most recent <see cref="LogQuery.Limit"/> matching entries,
    /// oldest first. Throws with an actionable message if the store can't be
    /// read; an empty or not-yet-created store is an empty result, not an error.
    /// </summary>
    IReadOnlyList<LogEntry> Query(LogQuery query);
}
