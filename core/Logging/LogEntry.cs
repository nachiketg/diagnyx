namespace Diagnyx.Core.Logging;

internal record LogEntry(
    string Timestamp,
    string Level,
    string Message,
    string Source,
    string? ContextJson,
    string? TraceId,
    string? SpanId
);
