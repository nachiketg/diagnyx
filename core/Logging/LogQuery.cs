namespace Diagnyx.Core.Logging;

/// <summary>
/// A "diagnyx query" request. Since/Until are canonical timestamps
/// (TimestampUtil.Format, inclusive bounds); Level is lowercase. Every sink
/// applies these same rules: Source and Contains are case-insensitive, and
/// Contains matches the message or the JSON-encoded context.
/// </summary>
internal sealed record LogQuery(
    string? Since,
    string? Until,
    string? Level,
    string? Source,
    string? Contains,
    int Limit)
{
    // Used by sinks that filter in memory (file). RDBMS sinks express the
    // same rules in SQL -- keep the two in step.
    public bool Matches(LogEntry entry)
    {
        if (Since is not null && string.CompareOrdinal(entry.Timestamp, Since) < 0)
            return false;
        if (Until is not null && string.CompareOrdinal(entry.Timestamp, Until) > 0)
            return false;
        if (Level is not null && entry.Level != Level)
            return false;
        if (Source is not null && !string.Equals(entry.Source, Source, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Contains is not null
            && !entry.Message.Contains(Contains, StringComparison.OrdinalIgnoreCase)
            && entry.ContextJson?.Contains(Contains, StringComparison.OrdinalIgnoreCase) != true)
            return false;

        return true;
    }
}
