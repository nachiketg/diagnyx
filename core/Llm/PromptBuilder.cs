using System.Text;
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

    public static string BuildUserMessage(string question, IReadOnlyList<RankedEntry> candidates)
    {
        var sb = new StringBuilder();
        sb.Append("Question: ").Append(question).Append("\n\nLog entries:\n");

        for (var i = 0; i < candidates.Count; i++)
        {
            var entry = candidates[i].Entry;
            sb.Append(i + 1).Append(". [").Append(entry.Timestamp).Append("] ")
              .Append(entry.Level).Append(" (").Append(entry.Source).Append("): ")
              .Append(entry.Message);

            if (entry.ContextJson is not null)
                sb.Append(" context=").Append(entry.ContextJson);

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
