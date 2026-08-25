using System.Data.Common;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

/// <summary>
/// Template-method base for all RDBMS sinks.
/// Concrete engines override CreateConnection() and CreateTableSql.
/// The shared Write path handles table creation, parameter binding,
/// and error reporting so no logic is duplicated per engine.
/// </summary>
internal abstract class RdbmsSink(string connectionString) : ISink
{
    protected string ConnectionString { get; } = connectionString;

    // Same INSERT SQL works across all supported engines; engines differ
    // only in CREATE TABLE DDL and the DbConnection they provide.
    private const string InsertSql =
        "INSERT INTO diagnyx_logs " +
        "(timestamp, level, message, source, context, trace_id, span_id) " +
        "VALUES (@timestamp, @level, @message, @source, @context, @traceId, @spanId)";

    /// <summary>DDL that creates diagnyx_logs if it does not yet exist.</summary>
    protected abstract string CreateTableSql { get; }

    /// <summary>Open and return a ready-to-use connection for this engine.</summary>
    protected abstract DbConnection CreateConnection();

    public int Write(LogEntry entry)
    {
        try
        {
            using var conn = CreateConnection();
            conn.Open();
            EnsureTable(conn);
            Insert(conn, entry);
            return 0;
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to write log entry: {ex.Message}");
            return 1;
        }
    }

    private void EnsureTable(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = CreateTableSql;
        cmd.ExecuteNonQuery();
    }

    // DbCommand.CreateParameter() creates the provider-specific DbParameter
    // without referencing any concrete type, keeping this base AOT-safe.
    private static void Insert(DbConnection conn, LogEntry entry)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = InsertSql;
        AddParam(cmd, "@timestamp", entry.Timestamp);
        AddParam(cmd, "@level",     entry.Level);
        AddParam(cmd, "@message",   entry.Message);
        AddParam(cmd, "@source",    entry.Source);
        AddParam(cmd, "@context",   entry.ContextJson);
        AddParam(cmd, "@traceId",   entry.TraceId);
        AddParam(cmd, "@spanId",    entry.SpanId);
        cmd.ExecuteNonQuery();
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
