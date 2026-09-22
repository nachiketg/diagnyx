using System.Data.Common;
using Diagnyx.Core.Logging;

namespace Diagnyx.Core.Sinks;

internal abstract class RdbmsSink(string connectionString, RdbmsRetentionPolicy? retention = null) : ISink, IQueryableSink
{
    protected string ConnectionString { get; } = connectionString;

    private const string InsertSql =
        "INSERT INTO diagnyx_logs " +
        "(timestamp, level, message, source, context, trace_id, span_id) " +
        "VALUES (@timestamp, @level, @message, @source, @context, @traceId, @spanId)";

    private const string DeleteOlderThanSql = "DELETE FROM diagnyx_logs WHERE timestamp < @cutoff";

    /// <summary>Human-readable engine name used in error messages (e.g. "PostgreSQL").</summary>
    protected abstract string EngineLabel { get; }

    /// <summary>DDL that creates diagnyx_logs if it does not yet exist.</summary>
    protected abstract string CreateTableSql { get; }

    /// <summary>Open and return a ready-to-use connection for this engine.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>
    /// The newest-first, capped SELECT for a query. The only piece of the query
    /// SQL that differs between engines: most cap with LIMIT, SQL Server with TOP.
    /// </summary>
    protected virtual string SelectSql(string whereClause) =>
        "SELECT timestamp, level, message, source, context, trace_id, span_id " +
        $"FROM diagnyx_logs {whereClause} ORDER BY timestamp DESC, id DESC LIMIT @limit";

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
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"error: {EngineLabel} write failed: {ex.Message}");
                return 1;
            }

            // Retention is a secondary concern: the entry above already made
            // it in, so a cleanup problem is a warning, never a reason to
            // report this write as failed.
            if (retention?.ShouldCheck() == true)
            {
                try
                {
                    DeleteOlderThan(conn, retention.Cutoff());
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"warning: {EngineLabel} retention cleanup failed: {ex.Message}");
                }
            }

            return 0;
        }
    }

    public IReadOnlyList<LogEntry> Query(LogQuery query)
    {
        DbConnection conn;
        try
        {
            conn = CreateConnection();
            conn.Open();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"could not connect to {EngineLabel}: {ex.Message}", ex);
        }

        using (conn)
        {
            // A fresh database has no table yet; creating it makes "nothing
            // logged yet" an empty result instead of an error. A read-only user
            // may not be allowed to create it -- fine, the SELECT below will
            // still work if the table exists.
            try { EnsureTable(conn); }
            catch (Exception) { }

            try
            {
                return ReadEntries(conn, query);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{EngineLabel} query failed: {ex.Message}", ex);
            }
        }
    }

    private IReadOnlyList<LogEntry> ReadEntries(DbConnection conn, LogQuery query)
    {
        using var cmd = conn.CreateCommand();

        // Mirrors LogQuery.Matches. Timestamps are fixed-width ISO 8601 text, so
        // string comparison is chronological. Source and Contains are lowered on
        // both sides because default collations differ per engine (case-insensitive
        // on MySQL/SQL Server, case-sensitive on PostgreSQL/SQLite).
        var conditions = new List<string>();
        if (query.Since is not null)
        {
            conditions.Add("timestamp >= @since");
            AddParam(cmd, "@since", query.Since);
        }
        if (query.Until is not null)
        {
            conditions.Add("timestamp <= @until");
            AddParam(cmd, "@until", query.Until);
        }
        if (query.Level is not null)
        {
            conditions.Add("level = @level");
            AddParam(cmd, "@level", query.Level);
        }
        if (query.Source is not null)
        {
            conditions.Add("LOWER(source) = @source");
            AddParam(cmd, "@source", query.Source.ToLowerInvariant());
        }
        if (query.Contains is not null)
        {
            conditions.Add("(LOWER(message) LIKE @contains ESCAPE '!' OR LOWER(context) LIKE @contains ESCAPE '!')");
            AddParam(cmd, "@contains", "%" + EscapeLike(query.Contains.ToLowerInvariant()) + "%");
        }

        cmd.CommandText = SelectSql(conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions));
        AddParam(cmd, "@limit", query.Limit);

        var entries = new List<LogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new LogEntry(
                Timestamp:   reader.GetString(0),
                Level:       reader.GetString(1),
                Message:     reader.GetString(2),
                Source:      reader.GetString(3),
                ContextJson: reader.IsDBNull(4) ? null : reader.GetString(4),
                TraceId:     reader.IsDBNull(5) ? null : reader.GetString(5),
                SpanId:      reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        // Fetched newest-first so the cap keeps the most recent; report oldest-first.
        entries.Reverse();
        return entries;
    }

    // '!' is the ESCAPE character in the LIKE above: unlike a backslash, it
    // needs no extra escaping in MySQL string literals. '[' is a wildcard
    // class only on SQL Server; escaping it elsewhere is harmless.
    private static string EscapeLike(string text) =>
        text.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");

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

    private static void DeleteOlderThan(DbConnection conn, string cutoff)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = DeleteOlderThanSql;
        AddParam(cmd, "@cutoff", cutoff);
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
