namespace Diagnyx.Core.Llm;

/// <summary>
/// The LLM's answer plus the numbers (1-based, matching PromptBuilder's
/// numbering of the candidate entries) of the entries it says it actually
/// used as evidence. Empty CitedEntryNumbers means the answer is unsupported
/// -- either the model said so explicitly, or its response couldn't be
/// parsed as the structured {"answer", "citedEntries"} shape we asked for.
/// </summary>
internal sealed record AskResult(string Answer, IReadOnlyList<int> CitedEntryNumbers);
