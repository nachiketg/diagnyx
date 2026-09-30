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
/// natural-language answer. Deliberately no --since/--until/--source/--limit
/// flags yet -- scoping ask queries is a separate, later concern; this
/// always retrieves against the whole configured sink.
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

        string answer;
        try
        {
            answer = LlmClient.Ask(llm.BaseUrl, llm.Model, apiKey, userMessage);
        }
        catch (Exception ex)
        {
            return Fail($"LLM request failed: {ex.Message}");
        }

        Console.WriteLine(answer);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
