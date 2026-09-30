using System.Text;
using Diagnyx.Core.Retrieval;

namespace Diagnyx.Core.Llm;

/// <summary>
/// Builds the system and user messages "diagnyx ask" sends to the LLM, from
/// a question and its retrieved candidate entries. Deliberately minimal --
/// a richer grounded template (stricter grounding rules, few-shot examples)
/// is a separate, later concern. Entries are numbered here so the model can
/// cite them back by number in its structured response.
/// </summary>
internal static class PromptBuilder
{
    public const string SystemMessage =
        "You are a log analysis assistant. Answer the user's question using " +
        "only the numbered log entries provided below. " +
        "Respond with a single JSON object of the exact shape " +
        "{\"answer\": <string>, \"citedEntries\": <array of integers>} and nothing else. " +
        "\"citedEntries\" must list only the numbers of the entries you actually " +
        "relied on as evidence for \"answer\". If the entries don't contain enough " +
        "information to answer, say so in \"answer\" and return an empty " +
        "\"citedEntries\" array instead of guessing.";

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
