using System.Diagnostics;
using System.Text.Json;

namespace Diagnyx;

/// <summary>
/// Thin .NET client for the Diagnyx structured logging CLI.
/// Each method spawns <c>diagnyx log</c> in a subprocess and returns the exit code (0 = success, 1 = failure).
/// </summary>
/// <remarks>
/// Binary resolution order:
/// <list type="number">
///   <item>The <c>binaryPath</c> constructor argument, when supplied.</item>
///   <item>The <c>DIAGNYX_PATH</c> environment variable.</item>
///   <item>The system PATH (looks for <c>diagnyx</c> or <c>diagnyx.exe</c>).</item>
/// </list>
/// </remarks>
public sealed class DiagnyxLogger
{
    private readonly string _source;
    private readonly CliRunner _runner;

    /// <param name="source">Application or service name written to the <c>source</c> field of every log entry.</param>
    /// <param name="binaryPath">
    /// Explicit path to the diagnyx binary. Leave null to use
    /// DIAGNYX_PATH env var or PATH discovery.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> is null or whitespace.</exception>
    /// <remarks>
    /// If the diagnyx binary cannot be found or fails to run, log calls do not throw:
    /// they print a warning to <see cref="Console.Error"/> and return <c>1</c>.
    /// </remarks>
    public DiagnyxLogger(string source, string? binaryPath = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("source must not be empty.", nameof(source));

        _source = source;
        _runner = new CliRunner(binaryPath);
    }

    /// <summary>Writes a <c>debug</c>-level log entry.</summary>
    public int Debug(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null) => Log("debug", message, context, traceContext);

    /// <summary>Writes an <c>info</c>-level log entry.</summary>
    public int Info(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null) => Log("info", message, context, traceContext);

    /// <summary>Writes a <c>warn</c>-level log entry.</summary>
    public int Warn(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null) => Log("warn", message, context, traceContext);

    /// <summary>Writes an <c>error</c>-level log entry.</summary>
    public int Error(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null) => Log("error", message, context, traceContext);

    /// <summary>Writes a <c>fatal</c>-level log entry.</summary>
    public int Fatal(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null) => Log("fatal", message, context, traceContext);

    /// <summary>
    /// Writes a log entry at the specified level.
    /// </summary>
    /// <param name="level">One of: <c>debug</c>, <c>info</c>, <c>warn</c>, <c>error</c>, <c>fatal</c>.</param>
    /// <param name="message">Human-readable description of the event. Must not be empty.</param>
    /// <param name="context">
    /// Arbitrary structured metadata. Accepts any object (serialized with System.Text.Json)
    /// or a pre-serialized JSON object string.
    /// </param>
    /// <param name="traceContext">
    /// Explicit (TraceId, SpanId) pair to correlate this entry with a trace. When omitted,
    /// falls back to <see cref="Activity.Current"/> (the ambient, AsyncLocal-based trace
    /// context .NET and OpenTelemetry instrumentation already populate) when one is active.
    /// </param>
    /// <returns>0 on success, 1 on failure.</returns>
    public int Log(string level, string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)
    {
        var args = BuildArgs(level, message, context);
        return _runner.Run(args, ResolveTraceparent(traceContext));
    }

    private static string? ResolveTraceparent((string TraceId, string SpanId)? traceContext)
    {
        if (traceContext is { } explicitContext)
            return $"00-{explicitContext.TraceId}-{explicitContext.SpanId}-01";

        var activity = Activity.Current;
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C)
            return null;

        return $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
    }

    private string[] BuildArgs(string level, string message, object? context)
    {
        var args = new List<string>
        {
            "log",
            "--level",   level,
            "--message", message,
            "--source",  _source,
        };

        if (context != null)
        {
            var json = context is string s ? s : JsonSerializer.Serialize(context);
            args.Add("--context");
            args.Add(json);
        }

        return args.ToArray();
    }
}
