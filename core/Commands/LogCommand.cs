using System.Text.Json;
using Diagnyx.Core.Config;
using Diagnyx.Core.Logging;
using Diagnyx.Core.Sinks;

namespace Diagnyx.Core.Commands;

internal static class LogCommand
{
    internal static int Run(string[] args)
    {
        string? level = null, message = null, context = null, source = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--level"   when i + 1 < args.Length: level   = args[++i]; break;
                case "--message" when i + 1 < args.Length: message = args[++i]; break;
                case "--context" when i + 1 < args.Length: context = args[++i]; break;
                case "--source"  when i + 1 < args.Length: source  = args[++i]; break;
            }
        }

        if (string.IsNullOrWhiteSpace(level))
            return Fail("--level is required. Valid values: debug, info, warn, error, fatal.");

        if (!LogLevel.IsValid(level))
            return Fail($"Invalid level '{level}'. Valid values: debug, info, warn, error, fatal.");

        if (string.IsNullOrWhiteSpace(message))
            return Fail("--message is required.");

        string? validatedContext = null;
        if (context is not null)
        {
            if (!TryValidateContext(context, out validatedContext, out var jsonError))
                return Fail($"--context is not valid JSON: {jsonError}");
        }

        var config = ConfigLoader.Load();
        source ??= config.Defaults?.Source ?? "app";

        var (traceId, spanId) = TraceContext.TryGetActive();

        var entry = new LogEntry(
            Timestamp:   DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Level:       level.ToLowerInvariant(),
            Message:     message,
            Source:      source,
            ContextJson: validatedContext,
            TraceId:     traceId,
            SpanId:      spanId
        );

        var sink = SinkFactory.Create(config);
        return sink.Write(entry);
    }

    private static bool TryValidateContext(string json, out string? normalized, out string? error)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                normalized = null;
                error = $"must be a JSON object, got {doc.RootElement.ValueKind.ToString().ToLowerInvariant()}";
                return false;
            }
            normalized = json.Trim();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            normalized = null;
            error = ex.Message;
            return false;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
