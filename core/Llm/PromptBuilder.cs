using System.Text;
using Diagnyx.Core.Logging;
using Diagnyx.Core.Retrieval;

namespace Diagnyx.Core.Llm;

/// <summary>
/// Builds the system and user messages "diagnyx ask" sends to the LLM, from
/// a question and its retrieved candidate entries. Entries are numbered here
/// so the model can cite them back by number in its structured response.
/// </summary>
internal static class PromptBuilder
{
    // Grounding rules, in order: (1) the entries are the only source of
    // truth -- explicitly ruled out is filling gaps with the kind of
    // general/prior knowledge that makes a guess read like analysis; (2)
    // correlation in the log isn't causation unless an entry says so
    // directly, since two nearby errors inviting a causal story is exactly
    // the hallucination this ticket exists to prevent; (3) the structured
    // {"answer", "citedEntries"} response contract from DX-045; (4)
    // insufficient OR merely-suggestive evidence must be named as such, not
    // smoothed over with a hedged-sounding guess.
    public const string SystemMessage =
        "You are a log analysis assistant. Answer the user's question using " +
        "ONLY the numbered log entries provided below -- never your own general " +
        "or prior knowledge about what commonly causes this kind of error, even " +
        "if it seems like an obvious or likely explanation. " +
        "If entries show two things happening near each other, describe what " +
        "they show; do not assert that one caused the other unless an entry " +
        "says so directly. " +
        "Respond with a single JSON object of the exact shape " +
        "{\"answer\": <string>, \"citedEntries\": <array of integers>} and nothing else. " +
        "\"citedEntries\" must list only the numbers of the entries you actually " +
        "relied on as evidence for \"answer\". " +
        "If the entries don't contain enough information to answer -- including " +
        "if they're merely suggestive but not conclusive -- say so plainly in " +
        "\"answer\" and return an empty \"citedEntries\" array. Never guess or " +
        "fill gaps with speculation.";

    /// <summary>
    /// Default character budget for the "Log entries:" section, used when
    /// llm.maxContextChars is unset or non-positive. ~8000 characters is a
    /// deliberately conservative ~2000 tokens, leaving headroom for the
    /// system message, the question, and the model's own reply even on a
    /// modest context window.
    /// </summary>
    public const int DefaultMaxContextChars = 8000;

    /// <summary>
    /// Builds the user message, numbering candidates in rank order so the
    /// model can cite them back by number. A large incident can retrieve
    /// entries whose combined text would risk exceeding the model's context
    /// window, so entries are included in full only up to maxContextChars;
    /// anything beyond that is left out of the numbered list entirely (so it
    /// can never be cited) and rolled into one summary line that still names
    /// the excluded entries' sources and time range, deterministically --
    /// no second LLM call, no lossy merging of included entries. At least
    /// one entry is always included in full, even if it alone exceeds the
    /// budget, so a too-small budget can't reduce ask to no evidence at all.
    /// </summary>
    public static string BuildUserMessage(string question, IReadOnlyList<RankedEntry> candidates, int maxContextChars = DefaultMaxContextChars)
    {
        if (maxContextChars <= 0)
            maxContextChars = DefaultMaxContextChars;

        var sb = new StringBuilder();
        sb.Append("Question: ").Append(question).Append("\n\nLog entries:\n");

        var entriesLength = 0;
        var includedCount = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var line = BuildEntryLine(i + 1, candidates[i].Entry);
            if (includedCount > 0 && entriesLength + line.Length > maxContextChars)
                break;

            sb.Append(line);
            entriesLength += line.Length;
            includedCount++;
        }

        if (includedCount < candidates.Count)
            AppendOverflowSummary(sb, candidates, includedCount);

        return sb.ToString();
    }

    private static string BuildEntryLine(int number, LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append(number).Append(". [").Append(entry.Timestamp).Append("] ")
          .Append(entry.Level).Append(" (").Append(entry.Source).Append("): ")
          .Append(entry.Message);

        if (entry.ContextJson is not null)
            sb.Append(" context=").Append(entry.ContextJson);

        sb.Append('\n');
        return sb.ToString();
    }

    private static void AppendOverflowSummary(StringBuilder sb, IReadOnlyList<RankedEntry> candidates, int includedCount)
    {
        var excludedCount = candidates.Count - includedCount;
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        string? earliest = null, latest = null;

        for (var i = includedCount; i < candidates.Count; i++)
        {
            var entry = candidates[i].Entry;
            sources.Add(entry.Source);
            if (earliest is null || string.CompareOrdinal(entry.Timestamp, earliest) < 0)
                earliest = entry.Timestamp;
            if (latest is null || string.CompareOrdinal(entry.Timestamp, latest) > 0)
                latest = entry.Timestamp;
        }

        sb.Append("(+").Append(excludedCount).Append(" more retrieved ")
          .Append(excludedCount == 1 ? "entry" : "entries")
          .Append(" not shown individually -- sources: ").Append(string.Join(", ", sources))
          .Append("; time range: ").Append(earliest).Append(" to ").Append(latest)
          .Append(". Narrow --since/--until/--source, or ask a more specific question, to include them.)\n");
    }
}
