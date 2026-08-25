using System.Data.Common;
using Npgsql;

namespace Diagnyx.Core.Sinks;

internal sealed class PostgresSink(string connectionString) : RdbmsSink(connectionString)
{
    // timestamp and context stored as TEXT so plain string parameters bind
    // without Npgsql type inference. Cast in queries when needed:
    //   timestamp::timestamptz, context::jsonb
    protected override string CreateTableSql => """
        CREATE TABLE IF NOT EXISTS diagnyx_logs (
            id        BIGSERIAL    PRIMARY KEY,
            timestamp TEXT         NOT NULL,
            level     VARCHAR(10)  NOT NULL,
            message   TEXT         NOT NULL,
            source    VARCHAR(255) NOT NULL,
            context   TEXT,
            trace_id  VARCHAR(64),
            span_id   VARCHAR(32)
        )
        """;

    protected override DbConnection CreateConnection() =>
        new NpgsqlConnection(ConnectionString);
}
