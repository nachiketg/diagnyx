namespace Diagnyx.Core.Config;

internal sealed class DiagnyxConfig
{
    public SinkConfig Sink { get; set; } = new();
    public MetricsConfig? Metrics { get; set; }
    public LlmConfig? Llm { get; set; }
    public DefaultsConfig? Defaults { get; set; }
}

internal sealed class SinkConfig
{
    public string Type { get; set; } = "file";

    /// <summary>
    /// Fan-out: write to every listed sink instead of just Type. When set
    /// and non-empty, this takes priority over Type entirely. Each type's
    /// own config still comes from the matching field below (File, Otlp,
    /// ...) -- Types only changes which of them are active, not how any one
    /// of them is configured. Unset (the default) means single-sink,
    /// exactly as before this existed.
    /// </summary>
    public List<string>? Types { get; set; }

    public FileSinkConfig? File { get; set; }
    public SqliteSinkConfig? Sqlite { get; set; }
    public RdbmsSinkConfig? Postgres { get; set; }
    public RdbmsSinkConfig? Mysql { get; set; }
    public RdbmsSinkConfig? Mssql { get; set; }
    public OtlpSinkConfig? Otlp { get; set; }
    public LokiSinkConfig? Loki { get; set; }
}

internal sealed class FileSinkConfig
{
    public string Path { get; set; } = "~/.diagnyx/logs/diagnyx.log";

    /// <summary>
    /// Rotate once the active file is at least this old, e.g. "7d", "24h".
    /// A duration string (see DurationParser), not a config type of its own,
    /// so it reads the same as "diagnyx query"'s --since/--until. Unset (the
    /// default) means no age-based rotation.
    /// </summary>
    public string? MaxAge { get; set; }

    /// <summary>
    /// Rotate once the active file is at least this many bytes. Checked
    /// before each write, so the file can exceed this by up to one entry
    /// before the next write rotates it. Unset (the default) means no
    /// size-based rotation.
    /// </summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>
    /// How many rotated files to keep (diagnyx.log.1 .. diagnyx.log.N);
    /// anything older is deleted. Only takes effect once MaxAge and/or
    /// MaxSizeBytes turns rotation on. 0 keeps no rotated files at all.
    /// </summary>
    public int MaxBackups { get; set; } = 5;
}

/// <summary>
/// Shared by every RDBMS sink's config type (SqliteSinkConfig, RdbmsSinkConfig)
/// so SinkFactory can build a retention policy from any of them the same way.
/// </summary>
internal interface IRetentionConfig
{
    /// <summary>
    /// Delete rows older than this on some writes, e.g. "30d", "720h". A
    /// duration string (see DurationParser). Unset (the default) means no
    /// retention cleanup at all -- diagnyx_logs grows unbounded.
    /// </summary>
    string? MaxAge { get; }

    /// <summary>
    /// Chance (0.0-1.0) that a given write also runs the cleanup DELETE.
    /// There is no background process, so this -- not a timer -- is what
    /// keeps cleanup from adding a query to every single write. 1 means
    /// every write; useful for a low-volume sink or a deterministic test.
    /// </summary>
    double RetentionCheckProbability { get; }
}

internal sealed class SqliteSinkConfig : IRetentionConfig
{
    public string Path { get; set; } = "~/.diagnyx/diagnyx.db";
    public string? MaxAge { get; set; }
    public double RetentionCheckProbability { get; set; } = 0.01;
}

internal sealed class RdbmsSinkConfig : IRetentionConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public string? MaxAge { get; set; }
    public double RetentionCheckProbability { get; set; } = 0.01;
}

internal sealed class OtlpSinkConfig
{
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Number of retries after an initial failed export attempt (so 3 means
    /// up to 4 attempts total), with exponential backoff between attempts.
    /// Only failures classified as transient are retried -- see
    /// OtlpSink.IsRetryableStatus.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

internal sealed class LokiSinkConfig
{
    public string Endpoint { get; set; } = string.Empty;
}

/// <summary>
/// Independent of sink.type -- log-count metrics are tracked (if enabled)
/// no matter which sink is active. Disabled by default: "diagnyx log" does
/// no counter-store work at all unless this is turned on.
/// </summary>
internal sealed class MetricsConfig
{
    public bool Enabled { get; set; } = false;
    public string Path { get; set; } = "~/.diagnyx/metrics.db";
}

/// <summary>
/// Configures "diagnyx ask"'s LLM call. Both fields are required to use
/// "ask" at all -- there is no default provider or model, since a generic
/// OpenAI-compatible endpoint has no universally sensible one. The API key
/// deliberately isn't a config field: it comes from the DIAGNYX_LLM_API_KEY
/// environment variable instead, so it's never something that could end up
/// committed inside diagnyx.config.json.
/// </summary>
internal sealed class LlmConfig
{
    /// <summary>
    /// Base URL of an OpenAI-compatible chat completions API, e.g.
    /// "https://api.openai.com/v1". "/chat/completions" is appended
    /// automatically unless the URL already ends with it.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Model name as the configured endpoint expects it, e.g. "gpt-4o-mini".</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Character budget for the retrieved log entries included in the
    /// prompt (see PromptBuilder). A large incident can retrieve entries
    /// whose combined text would risk exceeding the model's context
    /// window; entries beyond this budget are left out of the prompt and
    /// rolled into a single summary line instead of being sent in full.
    /// Unset, or any non-positive value, falls back to
    /// PromptBuilder.DefaultMaxContextChars.
    /// </summary>
    public int? MaxContextChars { get; set; }

    /// <summary>
    /// Context JSON key names to mask before log entries are sent to the
    /// LLM (see ContextRedactor) -- e.g. ["email", "apiKey", "ssn"].
    /// Matched case-insensitively, at any depth within a context object.
    /// Unset or empty means no redaction, same as before this existed:
    /// this is opt-in, not an automatic blanket redaction. Only applies to
    /// the outbound LLM request; "diagnyx query"/"diagnyx retrieve" output
    /// is local and untouched.
    /// </summary>
    public List<string>? RedactContextFields { get; set; }
}

internal sealed class DefaultsConfig
{
    public string Source { get; set; } = "app";
}
