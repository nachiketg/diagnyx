using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Retrieval;

/// <summary>
/// One candidate entry paired with its relevance score (count of distinct
/// question terms found in the entry's message/context). Higher is more
/// relevant; 0 means no term overlap at all -- still a valid, if weak,
/// candidate, not filtered out, since ranking (not exclusion) is the point.
/// </summary>
internal sealed record RankedEntry(LogEntry Entry, int Score);
