using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Commands;

/// <summary>
/// diagnyx ask "&lt;question&gt;" [--since &lt;time&gt;] [--until &lt;time&gt;] [--source &lt;name&gt;] [--verbose]
/// diagnyx ask serve [--port &lt;port&gt;]
///
/// Parses CLI input and prints the result; the actual retrieve-prompt-ask
/// pipeline lives in AskService, shared with "ask serve"'s web UI so both
/// behave identically. --since/--until/--source narrow retrieval the same
/// way they narrow "diagnyx query" -- same parsing (CliTimeParser), same
/// matching rules. --verbose prints an estimated prompt token count to
/// stderr before the request is sent, so a surprisingly large request can
/// be noticed (and the command killed) before it's billed, not just after.
/// Deliberately no --limit flag yet -- AskService's RetrievalLimit stays
/// fixed for now.
/// </summary>
internal static class AskCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "serve")
            return AskServeCommand.Run(args[1..]);

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

        Action<int, int>? onPromptEstimated = verbose ? PrintTokenEstimate : null;
        var outcome = AskService.Run(question, since, until, source, onPromptEstimated);

        if (!outcome.Success)
            return Fail(outcome.Text);

        Console.WriteLine(outcome.Text);
        return 0;
    }

    /// <summary>
    /// Printed to stderr, not stdout -- stdout stays exactly the answer (and
    /// citations) on success, nothing else, matching every other diagnyx
    /// command. Invoked by AskService before the LLM call is made, so a
    /// surprisingly large estimate is visible before the request goes out,
    /// not after.
    /// </summary>
    private static void PrintTokenEstimate(int systemTokens, int userTokens)
    {
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
