using System.Data.Common;

namespace Diagnyx.Core.Sinks;

// Implemented in DX-014 (requires Microsoft.Data.SqlClient).
internal sealed class MssqlSink(string connectionString) : RdbmsSink(connectionString)
{
    // MSSQL lacks CREATE TABLE IF NOT EXISTS; use INFORMATION_SCHEMA check instead.
    protected override string CreateTableSql => """
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = 'diagnyx_logs'
        )
        BEGIN
            CREATE TABLE diagnyx_logs (
                id        INT IDENTITY(1,1)  PRIMARY KEY,
                timestamp DATETIMEOFFSET     NOT NULL,
                level     NVARCHAR(10)       NOT NULL,
                message   NVARCHAR(MAX)      NOT NULL,
                source    NVARCHAR(255)      NOT NULL,
                context   NVARCHAR(MAX),
                trace_id  NVARCHAR(64),
                span_id   NVARCHAR(32)
            )
        END
        """;

    protected override DbConnection CreateConnection() =>
        throw new NotSupportedException(
            "MSSQL sink requires the Microsoft.Data.SqlClient package. " +
            "Support is coming in a future release. See docs/CONFIG.md.");
}
