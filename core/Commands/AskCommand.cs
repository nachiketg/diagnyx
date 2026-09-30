using System.Text;
using Diagnyx.Core.Config;
using Diagnyx.Core.Llm;
using Diagnyx.Core.Retrieval;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

/// <summary>
/// diagnyx ask "&lt;question&gt;"
///
/// Retrieves relevant entries (LogRetriever, same as "diagnyx retrieve"),
/// sends them with the question to the configured LLM, and prints a
/// natural-language answer followed by the timestamp and a short excerpt of
/// each entry the model cited as evidence -- or, if it cited none, a clear
/// UNSUPPORTED label instead of letting the answer read as verified fact.
/// Deliberately no --since/--until/--source/--limit flags yet -- scoping ask
/// queries is a separate, later concern; this always retrieves against the
/// whole configured sink.
/// </summary>
internal static class AskCommand
{
    // Matches RetrieveCommand's own default -- a reasonable-size grounding
    // set without a dedicated flag to control it yet.
    private const int RetrievalLimit = 20;

    internal static int Run(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            return Fail("a question is required, e.g. diagnyx ask \"why did the payment service fail?\"");

        if (args.Length > 1)
            return Fail("diagnyx ask takes a single question argument -- quote it if it contains spaces.");

        var question = args[0];

        var config = ConfigLoader.Load();

        var llm = config.Llm;
        if (string.IsNullOrWhiteSpace(llm?.BaseUrl) || string.IsNullOrWhiteSpace(llm?.Model))
            return Fail(
                "no LLM is configured. Set \"llm\": { \"baseUrl\": \"...\", \"model\": \"...\" } " +
                "in your config. See docs/CONFIG.md.");

        var apiKey = Environment.GetEnvironmentVariable("DIAGNYX_LLM_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return Fail("the DIAGNYX_LLM_API_KEY environment variable is not set. See docs/CONFIG.md.");

        var sink = SinkFactory.Create(config);
        if (sink is not IQueryableSink queryable)
            return Fail(
                $"the '{config.Sink.Type ?? "file"}' sink is write-only, so 'diagnyx ask' can't read from it. " +
                "Queryable sinks: file, sqlite, postgres, mysql, mssql.");

        var candidates = LogRetriever.Retrieve(
            queryable, new RetrievalRequest(question, Since: null, Until: null, Source: null, RetrievalLimit));

        if (candidates.Count == 0)
            return Fail("no log entries found to answer this question. Check your sink has data.");

        var userMessage = PromptBuilder.BuildUserMessage(question, candidates);

        AskResult result;
        try
        {
            result = LlmClient.Ask(llm.BaseUrl, llm.Model, apiKey, userMessage);
        }
        catch (Exception ex)
        {
            return Fail($"LLM request failed: {ex.Message}");
        }

        Console.WriteLine(result.Answer);
        Console.WriteLine();
        Console.WriteLine(FormatCitations(result.CitedEntryNumbers, candidates));
        return 0;
    }

    /// <summary>
    /// Entry numbers are 1-based and refer back to PromptBuilder's numbering
    /// of "candidates". Numbers outside that range (a model hallucinating a
    /// citation) are dropped rather than trusted; duplicates collapse to one
    /// listing per entry. If nothing valid is left, the answer is labeled
    /// unsupported -- an unverifiable citation is no citation at all.
    /// </summary>
    private static string FormatCitations(IReadOnlyList<int> citedEntryNumbers, IReadOnlyList<RankedEntry> candidates)
    {
        var cited = citedEntryNumbers
            .Distinct()
            .Where(number => number >= 1 && number <= candidates.Count)
            .OrderBy(number => number)
            .ToArray();

        if (cited.Length == 0)
            return "UNSUPPORTED: no log entries were cited as evidence for this answer.";

        var sb = new StringBuilder("Cited entries:");
        foreach (var number in cited)
        {
            var entry = candidates[number - 1].Entry;
            sb.Append('\n').Append("  [").Append(number).Append("] ")
              .Append(entry.Timestamp).Append(" -- ").Append(Excerpt(entry.Message));
        }

        return sb.ToString();
    }

    private const int ExcerptMaxLength = 80;

    private static string Excerpt(string message)
    {
        return message.Length <= ExcerptMaxLength
            ? message
            : string.Concat(message.AsSpan(0, ExcerptMaxLength - 3), "...");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
