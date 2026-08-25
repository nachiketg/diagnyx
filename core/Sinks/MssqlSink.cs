using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Diagnyx.Core.Sinks;

internal sealed class MssqlSink(string connectionString) : RdbmsSink(connectionString)
{
    // MSSQL lacks CREATE TABLE IF NOT EXISTS; OBJECT_ID check is the idiomatic
    // T-SQL alternative. timestamp stored as NVARCHAR(50) so plain string
    // parameters bind without type coercion.
    protected override string CreateTableSql => """
        IF OBJECT_ID('diagnyx_logs', 'U') IS NULL
        BEGIN
            CREATE TABLE diagnyx_logs (
                id        INT IDENTITY(1,1)  PRIMARY KEY,
                timestamp NVARCHAR(50)       NOT NULL,
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
        new SqlConnection(ConnectionString);
}
