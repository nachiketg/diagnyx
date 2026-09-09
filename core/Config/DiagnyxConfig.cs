namespace Diagnyx.Core.Config;

internal sealed class DiagnyxConfig
{
    public SinkConfig Sink { get; set; } = new();
    public DefaultsConfig? Defaults { get; set; }
}

internal sealed class SinkConfig
{
    public string Type { get; set; } = "file";
    public FileSinkConfig? File { get; set; }
    public SqliteSinkConfig? Sqlite { get; set; }
    public RdbmsSinkConfig? Postgres { get; set; }
    public RdbmsSinkConfig? Mysql { get; set; }
    public RdbmsSinkConfig? Mssql { get; set; }
    public OtlpSinkConfig? Otlp { get; set; }
}

internal sealed class FileSinkConfig
{
    public string Path { get; set; } = "~/.diagnyx/logs/diagnyx.log";
}

internal sealed class SqliteSinkConfig
{
    public string Path { get; set; } = "~/.diagnyx/diagnyx.db";
}

internal sealed class RdbmsSinkConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

internal sealed class OtlpSinkConfig
{
    public string Endpoint { get; set; } = string.Empty;
}

internal sealed class DefaultsConfig
{
    public string Source { get; set; } = "app";
}
