using Microsoft.Data.Sqlite;

namespace Diagnyx.Core.Metrics;

/// <summary>
/// A small local SQLite-backed counter store: one row per (level, source)
/// pair, incremented atomically by every "diagnyx log" call and read back
/// by "diagnyx metrics serve" on each scrape. SQLite (already a project
/// dependency via the sqlite sink) handles the concurrent-writer safety a
/// plain counts file would need to reimplement.
/// </summary>
internal static class MetricsStore
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS diagnyx_metrics_counters (
            level  TEXT    NOT NULL,
            source TEXT    NOT NULL,
            count  INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (level, source)
        )
        """;

    private const string UpsertSql = """
        INSERT INTO diagnyx_metrics_counters (level, source, count)
        VALUES (@level, @source, 1)
        ON CONFLICT(level, source) DO UPDATE SET count = count + 1
        """;

    public static void Increment(string dbPath, string level, string source)
    {
        using var conn = Open(dbPath);

        using var upsert = conn.CreateCommand();
        upsert.CommandText = UpsertSql;
        upsert.Parameters.AddWithValue("@level", level);
        upsert.Parameters.AddWithValue("@source", source);
        upsert.ExecuteNonQuery();
    }

    public static IReadOnlyList<(string Level, string Source, long Count)> ReadAll(string dbPath)
    {
        using var conn = Open(dbPath);

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT level, source, count FROM diagnyx_metrics_counters ORDER BY level, source";

        var results = new List<(string, string, long)>();
        using var reader = select.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));

        return results;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            // WAL + a busy timeout keep concurrent "diagnyx log" processes and
            // a running "diagnyx metrics serve" from stepping on each other.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using (var create = conn.CreateCommand())
        {
            create.CommandText = CreateTableSql;
            create.ExecuteNonQuery();
        }

        return conn;
    }
}
