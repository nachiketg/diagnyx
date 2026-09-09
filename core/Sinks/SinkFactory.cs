using Diagnyx.Core.Config;

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
            var t      => throw new InvalidOperationException(
                $"Unknown sink type '{t}'. Valid values: file, sqlite, postgres, mysql, mssql, otlp.")
        };
    }

    private static FileSink CreateFileSink(DiagnyxConfig config)
    {
        var rawPath = config.Sink.File?.Path ?? "~/.diagnyx/logs/diagnyx.log";
        return new FileSink(ConfigLoader.ExpandPath(rawPath));
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
        var endpoint = config.Sink.Otlp?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "sink.otlp.endpoint is required when sink.type is 'otlp'. " +
                "See docs/CONFIG.md.");
        return new OtlpSink(endpoint);
    }
}
