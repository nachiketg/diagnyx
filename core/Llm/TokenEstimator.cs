namespace Diagnyx.Core.Llm;

/// <summary>
/// A rough, deterministic token-count estimate for the English-ish text
/// "diagnyx ask" sends -- roughly 4 characters per token, the same rule of
/// thumb OpenAI's own docs use. Not a real tokenizer: exact counts vary by
/// model and vendor, and aren't worth a heavy dependency (or a network call
/// to a tokenizer endpoint) for an estimate whose job is to catch "this
/// request is unexpectedly huge" before it's sent, not to be exact.
/// </summary>
internal static class TokenEstimator
{
    private const double CharsPerToken = 4.0;

    public static int EstimateTokens(string text) =>
        (int)Math.Ceiling(text.Length / CharsPerToken);
}
