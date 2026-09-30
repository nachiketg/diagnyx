using System.Text;
using Diagnyx.Core.Retrieval;

namespace Diagnyx.Core.Llm;

/// <summary>
/// Builds the system and user messages "diagnyx ask" sends to the LLM, from
/// a question and its retrieved candidate entries. Deliberately minimal --
/// a richer grounded template (citation formatting, stricter grounding
/// rules) is a separate, later concern. Entries are numbered here so that
/// concern has something simple to hang citations off of.
/// </summary>
internal static class PromptBuilder
{
    public const string SystemMessage =
        "You are a log analysis assistant. Answer the user's question using " +
        "only the numbered log entries provided below. If they don't contain " +
        "enough information to answer, say so clearly instead of guessing.";

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
