using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Diagnyx.Core.Sinks;

internal sealed class SqliteSink(string dbPath) : RdbmsSink(dbPath)
{
    protected override string EngineLabel => "SQLite";

    protected override string CreateTableSql => """
        CREATE TABLE IF NOT EXISTS diagnyx_logs (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT    NOT NULL,
            level     TEXT    NOT NULL,
            message   TEXT    NOT NULL,
            source    TEXT    NOT NULL,
            context   TEXT,
            trace_id  TEXT,
            span_id   TEXT
        )
        """;

    protected override DbConnection CreateConnection() =>
        new SqliteConnection($"Data Source={ConnectionString}");
}
