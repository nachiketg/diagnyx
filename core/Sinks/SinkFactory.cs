using Diagnyx.Core.Config;

namespace Diagnyx.Core.Sinks;

internal static class SinkFactory
{
    internal static ISink Create(DiagnyxConfig config)
    {
        return (config.Sink.Type ?? "file").ToLowerInvariant() switch
        {
            "file"    => CreateFileSink(config),
            "sqlite"  => throw new InvalidOperationException("SQLite sink is not yet implemented. Coming in a future release."),
            "postgres"=> throw new InvalidOperationException("PostgreSQL sink is not yet implemented. Coming in a future release."),
            "mysql"   => throw new InvalidOperationException("MySQL sink is not yet implemented. Coming in a future release."),
            "mssql"   => throw new InvalidOperationException("MSSQL sink is not yet implemented. Coming in a future release."),
            var t     => throw new InvalidOperationException($"Unknown sink type '{t}'. Valid values: file, sqlite, postgres, mysql, mssql.")
        };
    }

    private static FileSink CreateFileSink(DiagnyxConfig config)
    {
        var rawPath = config.Sink.File?.Path ?? "~/.diagnyx/logs/diagnyx.log";
        return new FileSink(ConfigLoader.ExpandPath(rawPath));
    }
}
