using System.Data.Common;

namespace Diagnyx.Core.Sinks;

// Implemented in DX-012 (requires Npgsql).
internal sealed class PostgresSink(string connectionString) : RdbmsSink(connectionString)
{
    protected override string CreateTableSql => """
        CREATE TABLE IF NOT EXISTS diagnyx_logs (
            id        BIGSERIAL    PRIMARY KEY,
            timestamp TIMESTAMPTZ  NOT NULL,
            level     VARCHAR(10)  NOT NULL,
            message   TEXT         NOT NULL,
            source    VARCHAR(255) NOT NULL,
            context   JSONB,
            trace_id  VARCHAR(64),
            span_id   VARCHAR(32)
        )
        """;

    protected override DbConnection CreateConnection() =>
        throw new NotSupportedException(
            "PostgreSQL sink requires the Npgsql package. " +
            "Support is coming in a future release. See docs/CONFIG.md.");
}
