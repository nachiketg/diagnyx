using System.Data.Common;
using MySqlConnector;

namespace Diagnyx.Core.Sinks;

internal sealed class MySqlSink(string connectionString) : RdbmsSink(connectionString)
{
    protected override string EngineLabel => "MySQL";

    // timestamp stored as TEXT so plain string parameters bind without driver
    // type inference. context stored as TEXT for the same reason.
    // ENGINE=InnoDB and utf8mb4 are best-practice defaults for MySQL 8+.
    protected override string CreateTableSql => """
        CREATE TABLE IF NOT EXISTS diagnyx_logs (
            id        INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
            timestamp VARCHAR(50)  NOT NULL,
            level     VARCHAR(10)  NOT NULL,
            message   TEXT         NOT NULL,
            source    VARCHAR(255) NOT NULL,
            context   TEXT,
            trace_id  VARCHAR(64),
            span_id   VARCHAR(32)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
        """;

    protected override DbConnection CreateConnection() =>
        new MySqlConnection(ConnectionString);
}
