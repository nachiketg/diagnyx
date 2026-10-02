using System.Text;
using Diagnyx.Core.Config;
using Diagnyx.Core.Llm;
using Diagnyx.Core.Retrieval;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

/// <summary>
/// The core "ask" pipeline -- retrieve, build the prompt, call the LLM,
/// format the result -- shared between AskCommand (the CLI) and
/// AskServeCommand (the local web UI), so both behave identically: same
/// retrieval, same grounding, same citation formatting. Neither command
/// implements any of this itself; both just collect input (argv or an HTTP
/// form) and call Run.
/// </summary>
internal static class AskService
{
    // Matches RetrieveCommand's own default -- a reasonable-size grounding
    // set without a dedicated flag to control it yet.
    private const int RetrievalLimit = 20;

    /// <summary>
    /// Success carries the answer followed by its citations (or an
    /// UNSUPPORTED label), formatted exactly as the CLI prints it to
    /// stdout. Failure carries the same message the CLI would print to
    /// stderr. Callers decide where each goes -- console, HTTP response,
    /// wherever -- but never reformat it differently.
    /// </summary>
    internal sealed record Outcome(bool Success, string Text);

    /// <summary>
    /// onPromptEstimated, if given, is invoked with (systemMessageTokens,
    /// userMessageTokens) right after the prompt is built but before the
    /// LLM call -- so a caller that wants to surface this (the CLI's
    /// --verbose) can show it before the request is sent, not just after.
    /// </summary>
    internal static Outcome Run(
        string question,
        string? since,
        string? until,
        string? source,
        Action<int, int>? onPromptEstimated = null)
    {
        var config = ConfigLoader.Load();

        var llm = config.Llm;
        if (string.IsNullOrWhiteSpace(llm?.BaseUrl) && string.IsNullOrWhiteSpace(llm?.Model))
            return Failure(
                "no LLM is configured. Set \"llm\": { \"baseUrl\": \"...\", \"model\": \"...\" } " +
                "in your config. See docs/CONFIG.md.");
        if (string.IsNullOrWhiteSpace(llm?.BaseUrl))
            return Failure("llm.baseUrl is missing from config. Set \"llm\": { \"baseUrl\": \"...\" } in your config. See docs/CONFIG.md.");
        if (string.IsNullOrWhiteSpace(llm?.Model))
            return Failure("llm.model is missing from config. Set \"llm\": { \"model\": \"...\" } in your config. See docs/CONFIG.md.");

        // Optional: most local/self-hosted providers (Ollama, LM Studio, ...)
        // need no key at all. Hosted providers do, but that's enforced by
        // the provider itself (a 401/403 surfaces via "LLM request failed"),
        // not by diagnyx -- it has no way to know which providers require one.
        var apiKey = Environment.GetEnvironmentVariable("DIAGNYX_LLM_API_KEY");

        var sink = SinkFactory.Create(config);
        if (sink is not IQueryableSink queryable)
            return Failure(
                $"the '{config.Sink.Type ?? "file"}' sink is write-only, so ask can't read from it. " +
                "Queryable sinks: file, sqlite, postgres, mysql, mssql.");

        var candidates = LogRetriever.Retrieve(
            queryable, new RetrievalRequest(question, since, until, source, RetrievalLimit));

        if (candidates.Count == 0)
            return Failure("no log entries found to answer this question. Check your sink has data.");

        var maxContextChars = llm.MaxContextChars is > 0 ? llm.MaxContextChars.Value : PromptBuilder.DefaultMaxContextChars;
        var redactContextFields = new HashSet<string>(llm.RedactContextFields ?? [], StringComparer.OrdinalIgnoreCase);
        var userMessage = PromptBuilder.BuildUserMessage(question, candidates, maxContextChars, redactContextFields);

        if (onPromptEstimated is not null)
        {
            var systemTokens = TokenEstimator.EstimateTokens(PromptBuilder.SystemMessage);
            var userTokens = TokenEstimator.EstimateTokens(userMessage);
            onPromptEstimated(systemTokens, userTokens);
        }

        AskResult result;
        try
        {
            result = LlmClient.Ask(llm.BaseUrl, llm.Model, apiKey, userMessage);
        }
        catch (Exception ex)
        {
            return Failure($"LLM request failed: {ex.Message}");
        }

        var sb = new StringBuilder();
        sb.Append(result.Answer).Append("\n\n").Append(FormatCitations(result.CitedEntryNumbers, candidates));
        return new Outcome(true, sb.ToString());
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

    private static Outcome Failure(string message) => new(false, message);
}
