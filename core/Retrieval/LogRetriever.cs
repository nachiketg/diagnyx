using Diagnyx.Core.Logging;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Retrieval;

/// <summary>
/// Turns a free-text question plus optional time/source filters into a
/// bounded, relevance-ranked set of candidate log entries -- the grounded
/// data a future LLM-backed "diagnyx ask" reasons over. Reuses "diagnyx
/// query"'s own filtering (LogQuery, IQueryableSink) to fetch the candidate
/// pool; the only new piece here is ranking that pool by relevance to the
/// question instead of "diagnyx query"'s plain chronological order.
///
/// Ranking is a simple, deterministic term-overlap score -- no ML model or
/// embeddings, matching how the rest of Diagnyx favors small, explainable
/// building blocks over frameworks. Later Phase 3 tickets (LLM provider
/// config, prompt templates) are what actually reason about relevance;
/// this just needs to hand them a reasonable candidate set.
/// </summary>
internal static class LogRetriever
{
    // Large enough to rank meaningfully within a typical retrieval window
    // without pulling an unbounded amount out of the sink on every call.
    private const int CandidatePoolSize = 500;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "was", "were", "with", "that", "this",
        "from", "has", "have", "had", "did", "does", "not", "why", "what",
        "when", "where", "who", "how", "log", "logs", "entry", "entries",
    };

    public static IReadOnlyList<RankedEntry> Retrieve(IQueryableSink sink, RetrievalRequest request)
    {
        var candidates = sink.Query(new LogQuery(
            Since: request.Since,
            Until: request.Until,
            Level: null,
            Source: request.Source,
            Contains: null,
            Limit: CandidatePoolSize));

        var questionTerms = Tokenize(request.Question);
        var limit = Math.Max(0, request.Limit);

        return candidates
            .Select(entry => new RankedEntry(entry, Score(entry, questionTerms)))
            .OrderByDescending(ranked => ranked.Score)
            .ThenByDescending(ranked => ranked.Entry.Timestamp, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static int Score(LogEntry entry, IReadOnlySet<string> questionTerms)
    {
        if (questionTerms.Count == 0)
            return 0;

        var entryTerms = Tokenize(entry.Message);
        if (entry.ContextJson is not null)
            entryTerms.UnionWith(Tokenize(entry.ContextJson));

        return questionTerms.Count(entryTerms.Contains);
    }

    // No regex: manual scan splitting on non-alphanumeric characters, matching
    // how the rest of Diagnyx (DurationParser, FileRotationPolicy) parses text.
    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWordChar)
            {
                start = start < 0 ? i : start;
                continue;
            }

            if (start < 0)
                continue;

            var token = text[start..i].ToLowerInvariant();
            if (token.Length > 2 && !StopWords.Contains(token))
                tokens.Add(token);
            start = -1;
        }

        return tokens;
    }
}
