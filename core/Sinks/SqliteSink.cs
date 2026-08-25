using System.Data.Common;

namespace Diagnyx.Core.Sinks;

// Implemented in DX-011 (requires Microsoft.Data.Sqlite).
internal sealed class SqliteSink(string dbPath) : RdbmsSink(dbPath)
{
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
        throw new NotSupportedException(
            "SQLite sink requires the Microsoft.Data.Sqlite package. " +
            "Support is coming in a future release. See docs/CONFIG.md.");
}
