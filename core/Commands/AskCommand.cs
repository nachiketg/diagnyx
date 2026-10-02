using System.Text;
using Diagnyx.Core.Config;
using Diagnyx.Core.Llm;
using Diagnyx.Core.Logging;
using Diagnyx.Core.Retrieval;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

/// <summary>
/// diagnyx ask "&lt;question&gt;" [--since &lt;time&gt;] [--until &lt;time&gt;] [--source &lt;name&gt;] [--verbose]
///
/// Retrieves relevant entries (LogRetriever, same as "diagnyx retrieve"),
/// sends them with the question to the configured LLM, and prints a
/// natural-language answer followed by the timestamp and a short excerpt of
/// each entry the model cited as evidence -- or, if it cited none, a clear
/// UNSUPPORTED label instead of letting the answer read as verified fact.
/// --since/--until/--source narrow retrieval the same way they narrow
/// "diagnyx query" -- same parsing (CliTimeParser), same matching rules.
/// --verbose prints an estimated prompt token count to stderr before the
/// request is sent, so a surprisingly large request can be noticed (and the
/// command killed) before it's billed, not just after.
/// Deliberately no --limit flag yet -- RetrievalLimit stays fixed for now.
/// </summary>
internal static class AskCommand
{
    // Matches RetrieveCommand's own default -- a reasonable-size grounding
    // set without a dedicated flag to control it yet.
    private const int RetrievalLimit = 20;

    internal static int Run(string[] args)
    {
        string? question = null, since = null, until = null, source = null;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (question is not null)
                    return Fail("diagnyx ask takes a single question argument -- quote it if it contains spaces.");
                question = arg;
                continue;
            }

            if (arg == "--verbose")
            {
                verbose = true;
                continue;
            }

            if (arg is not ("--since" or "--until" or "--source"))
                return Fail($"unknown option '{arg}'. Run 'diagnyx --help' for usage.");

            if (i + 1 >= args.Length || args[i + 1].Length == 0)
                return Fail($"{arg} requires a non-empty value.");

            var value = args[++i];
            switch (arg)
            {
                case "--since":
                    if (!CliTimeParser.TryParse(value, out since))
                        return Fail(CliTimeParser.InvalidMessage(arg, value));
                    break;
                case "--until":
                    if (!CliTimeParser.TryParse(value, out until))
                        return Fail(CliTimeParser.InvalidMessage(arg, value));
                    break;
                case "--source":
                    source = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(question))
            return Fail("a question is required, e.g. diagnyx ask \"why did the payment service fail?\"");

        if (since is not null && until is not null && string.CompareOrdinal(since, until) > 0)
            return Fail("--since must not be later than --until.");

        var config = ConfigLoader.Load();

        var llm = config.Llm;
        if (string.IsNullOrWhiteSpace(llm?.BaseUrl) && string.IsNullOrWhiteSpace(llm?.Model))
            return Fail(
                "no LLM is configured. Set \"llm\": { \"baseUrl\": \"...\", \"model\": \"...\" } " +
                "in your config. See docs/CONFIG.md.");
        if (string.IsNullOrWhiteSpace(llm?.BaseUrl))
            return Fail("llm.baseUrl is missing from config. Set \"llm\": { \"baseUrl\": \"...\" } in your config. See docs/CONFIG.md.");
        if (string.IsNullOrWhiteSpace(llm?.Model))
            return Fail("llm.model is missing from config. Set \"llm\": { \"model\": \"...\" } in your config. See docs/CONFIG.md.");

        // Optional: most local/self-hosted providers (Ollama, LM Studio, ...)
        // need no key at all. Hosted providers do, but that's enforced by
        // the provider itself (a 401/403 surfaces via "LLM request failed"),
        // not by diagnyx -- it has no way to know which providers require one.
        var apiKey = Environment.GetEnvironmentVariable("DIAGNYX_LLM_API_KEY");

        var sink = SinkFactory.Create(config);
        if (sink is not IQueryableSink queryable)
            return Fail(
                $"the '{config.Sink.Type ?? "file"}' sink is write-only, so 'diagnyx ask' can't read from it. " +
                "Queryable sinks: file, sqlite, postgres, mysql, mssql.");

        var candidates = LogRetriever.Retrieve(
            queryable, new RetrievalRequest(question, since, until, source, RetrievalLimit));

        if (candidates.Count == 0)
            return Fail("no log entries found to answer this question. Check your sink has data.");

        var maxContextChars = llm.MaxContextChars is > 0 ? llm.MaxContextChars.Value : PromptBuilder.DefaultMaxContextChars;
        var redactContextFields = new HashSet<string>(llm.RedactContextFields ?? [], StringComparer.OrdinalIgnoreCase);
        var userMessage = PromptBuilder.BuildUserMessage(question, candidates, maxContextChars, redactContextFields);

        if (verbose)
            PrintTokenEstimate(userMessage);

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

    /// <summary>
    /// Printed to stderr, not stdout -- stdout stays exactly the answer (and
    /// citations) on success, nothing else, matching every other diagnyx
    /// command. Printed before LlmClient.Ask is called, so a surprisingly
    /// large estimate is visible before the request goes out, not after.
    /// </summary>
    private static void PrintTokenEstimate(string userMessage)
    {
        var systemTokens = TokenEstimator.EstimateTokens(PromptBuilder.SystemMessage);
        var userTokens = TokenEstimator.EstimateTokens(userMessage);
        Console.Error.WriteLine(
            $"estimated prompt tokens: ~{systemTokens + userTokens} " +
            $"(system message ~{systemTokens}, question + log entries ~{userTokens}). " +
            "A rough estimate (~4 chars/token) -- not an exact count, and excludes the model's reply.");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
