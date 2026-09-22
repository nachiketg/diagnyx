using Diagnyx.Core.Config;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal static class SinkFactory
{
    internal static ISink Create(DiagnyxConfig config)
    {
        return (config.Sink.Type ?? "file").ToLowerInvariant() switch
        {
            "file"     => CreateFileSink(config),
            "sqlite"   => CreateSqliteSink(config),
            "postgres" => CreatePostgresSink(config),
            "mysql"    => CreateMysqlSink(config),
            "mssql"    => CreateMssqlSink(config),
            "otlp"     => CreateOtlpSink(config),
            "loki"     => CreateLokiSink(config),
            var t      => throw new InvalidOperationException(
                $"Unknown sink type '{t}'. Valid values: file, sqlite, postgres, mysql, mssql, otlp, loki.")
        };
    }

    private static FileSink CreateFileSink(DiagnyxConfig config)
    {
        var fileConfig = config.Sink.File;
        var rawPath = fileConfig?.Path ?? "~/.diagnyx/logs/diagnyx.log";
        return new FileSink(ConfigLoader.ExpandPath(rawPath), BuildRotationPolicy(fileConfig));
    }

    private static FileRotationPolicy? BuildRotationPolicy(FileSinkConfig? config)
    {
        if (config is null || (config.MaxAge is null && config.MaxSizeBytes is null))
            return null;

        TimeSpan? maxAge = null;
        if (config.MaxAge is not null)
        {
            if (!DurationParser.TryParse(config.MaxAge, out var span))
                throw new InvalidOperationException(
                    $"sink.file.maxAge '{config.MaxAge}' is not a valid duration. " +
                    "Use a number plus a unit, e.g. \"7d\" or \"24h\" (units: s, m, h, d, w). " +
                    "See docs/CONFIG.md.");
            maxAge = span;
        }

        if (config.MaxSizeBytes is <= 0)
            throw new InvalidOperationException(
                $"sink.file.maxSizeBytes must be a positive number, got {config.MaxSizeBytes}. " +
                "See docs/CONFIG.md.");

        return new FileRotationPolicy(maxAge, config.MaxSizeBytes, config.MaxBackups);
    }

    private static SqliteSink CreateSqliteSink(DiagnyxConfig config)
    {
        var rawPath = config.Sink.Sqlite?.Path ?? "~/.diagnyx/diagnyx.db";
        return new SqliteSink(ConfigLoader.ExpandPath(rawPath));
    }

    private static PostgresSink CreatePostgresSink(DiagnyxConfig config)
    {
        var cs = config.Sink.Postgres?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "sink.postgres.connectionString is required when sink.type is 'postgres'. " +
                "See docs/CONFIG.md.");
        return new PostgresSink(cs);
    }

    private static MySqlSink CreateMysqlSink(DiagnyxConfig config)
    {
        var cs = config.Sink.Mysql?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "sink.mysql.connectionString is required when sink.type is 'mysql'. " +
                "See docs/CONFIG.md.");
        return new MySqlSink(cs);
    }

    private static MssqlSink CreateMssqlSink(DiagnyxConfig config)
    {
        var cs = config.Sink.Mssql?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "sink.mssql.connectionString is required when sink.type is 'mssql'. " +
                "See docs/CONFIG.md.");
        return new MssqlSink(cs);
    }

    private static OtlpSink CreateOtlpSink(DiagnyxConfig config)
    {
        var otlp = config.Sink.Otlp;
        if (string.IsNullOrWhiteSpace(otlp?.Endpoint))
            throw new InvalidOperationException(
                "sink.otlp.endpoint is required when sink.type is 'otlp'. " +
                "See docs/CONFIG.md.");
        return new OtlpSink(otlp.Endpoint, otlp.MaxRetries);
    }

    private static LokiSink CreateLokiSink(DiagnyxConfig config)
    {
        var endpoint = config.Sink.Loki?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "sink.loki.endpoint is required when sink.type is 'loki'. " +
                "See docs/CONFIG.md.");
        return new LokiSink(endpoint);
    }
}
