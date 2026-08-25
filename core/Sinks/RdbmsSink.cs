using System.Data.Common;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal abstract class RdbmsSink(string connectionString) : ISink
{
    protected string ConnectionString { get; } = connectionString;

    private const string InsertSql =
        "INSERT INTO diagnyx_logs " +
        "(timestamp, level, message, source, context, trace_id, span_id) " +
        "VALUES (@timestamp, @level, @message, @source, @context, @traceId, @spanId)";

    /// <summary>Human-readable engine name used in error messages (e.g. "PostgreSQL").</summary>
    protected abstract string EngineLabel { get; }

    /// <summary>DDL that creates diagnyx_logs if it does not yet exist.</summary>
    protected abstract string CreateTableSql { get; }

    /// <summary>Open and return a ready-to-use connection for this engine.</summary>
    protected abstract DbConnection CreateConnection();

    public int Write(LogEntry entry)
    {
        DbConnection conn;
        try
        {
            conn = CreateConnection();
            conn.Open();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"error: could not connect to {EngineLabel}: {ex.Message}");
            return 1;
        }

        using (conn)
        {
            try
            {
                EnsureTable(conn);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"error: {EngineLabel} table setup failed: {ex.Message}");
                return 1;
            }

            try
            {
                Insert(conn, entry);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"error: {EngineLabel} write failed: {ex.Message}");
                return 1;
            }
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
