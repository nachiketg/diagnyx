using System.Data.Common;

namespace Diagnyx.Core.Sinks;

// Implemented in DX-013 (requires MySqlConnector).
internal sealed class MySqlSink(string connectionString) : RdbmsSink(connectionString)
{
    protected override string CreateTableSql => """
        CREATE TABLE IF NOT EXISTS diagnyx_logs (
            id        INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
            timestamp DATETIME(3)  NOT NULL,
            level     VARCHAR(10)  NOT NULL,
            message   TEXT         NOT NULL,
            source    VARCHAR(255) NOT NULL,
            context   JSON,
            trace_id  VARCHAR(64),
            span_id   VARCHAR(32)
        )
        """;

    protected override DbConnection CreateConnection() =>
        throw new NotSupportedException(
            "MySQL sink requires the MySqlConnector package. " +
            "Support is coming in a future release. See docs/CONFIG.md.");
}
