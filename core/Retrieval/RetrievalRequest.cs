namespace Diagnyx.Core.Retrieval;

/// <summary>
/// A "diagnyx retrieve" request: a free-text question plus the same
/// time/source filters "diagnyx query" has. Since/Until are canonical
/// timestamps (see TimestampUtil), matching LogQuery's own contract.
///
/// Deliberately narrower than LogQuery: no Level or Contains filter. The
/// point of retrieval is to let Question (via relevance ranking) surface
/// what matters, including context around an error that a hard level
/// filter would have excluded -- not to narrow results by exact match
/// before ranking even runs.
/// </summary>
internal sealed record RetrievalRequest(
    string Question,
    string? Since,
    string? Until,
    string? Source,
    int Limit);
