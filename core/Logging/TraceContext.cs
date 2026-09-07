namespace Diagnyx.Core.Logging;

/// <summary>
/// Reads the W3C Trace Context propagated to this process via the TRACEPARENT
/// environment variable, so hosts that spawn the Diagnyx CLI as a subprocess
/// (the Node.js and .NET wrappers) get trace correlation without passing
/// --trace-id/--span-id explicitly or requiring wrapper changes -- child
/// processes inherit the parent's environment by default.
/// https://www.w3.org/TR/trace-context/#traceparent-header-field-values
/// </summary>
internal static class TraceContext
{
    internal static (string? TraceId, string? SpanId) TryGetActive() =>
        TryParse(Environment.GetEnvironmentVariable("TRACEPARENT"));

    internal static (string? TraceId, string? SpanId) TryParse(string? traceparent)
    {
        if (string.IsNullOrWhiteSpace(traceparent))
            return (null, null);

        var parts = traceparent.Trim().Split('-');
        if (parts.Length != 4)
            return (null, null);

        var (version, traceId, spanId, flags) = (parts[0], parts[1], parts[2], parts[3]);

        if (!IsLowerHex(version, 2) || version == "ff")
            return (null, null);
        if (!IsLowerHex(traceId, 32) || traceId == new string('0', 32))
            return (null, null);
        if (!IsLowerHex(spanId, 16) || spanId == new string('0', 16))
            return (null, null);
        if (!IsLowerHex(flags, 2))
            return (null, null);

        return (traceId, spanId);
    }

    private static bool IsLowerHex(string value, int length)
    {
        if (value.Length != length)
            return false;

        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }

        return true;
    }
}
