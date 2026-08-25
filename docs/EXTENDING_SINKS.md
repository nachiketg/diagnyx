# Adding a New Sink to Diagnyx

This document explains the sink abstraction so you can add new storage backends without touching the CLI, language wrappers, or any existing sink.

## Architecture

```
ISink
 ├── FileSink          (file, default)
 └── RdbmsSink         (abstract base for all database engines)
      ├── SqliteSink
      ├── PostgresSink
      ├── MySqlSink
      └── MssqlSink
```

`ISink` is the only contract the CLI knows about. `SinkFactory` maps a config `sink.type` value to a concrete instance. Everything else — connection management, table creation, parameter binding — lives inside the sink classes.

## ISink

```csharp
internal interface ISink
{
    int Write(LogEntry entry);
}
```

`Write` returns `0` on success and `1` on failure. It must never throw — surface errors to stderr and return `1`.

## Adding an RDBMS Engine

### 1. Inherit from `RdbmsSink`

`RdbmsSink` provides the complete `Write` implementation. You only override two members:

| Member | What to provide |
|--------|----------------|
| `CreateTableSql` | `CREATE TABLE IF NOT EXISTS diagnyx_logs (...)` DDL for your engine |
| `CreateConnection()` | Open and return a `DbConnection` for your engine |

```csharp
internal sealed class MyEngineSink(string connectionString) : RdbmsSink
{
    protected override string CreateTableSql => """
        CREATE TABLE IF NOT EXISTS diagnyx_logs (
            id        SERIAL       PRIMARY KEY,
            timestamp TEXT         NOT NULL,
            level     VARCHAR(10)  NOT NULL,
            message   TEXT         NOT NULL,
            source    VARCHAR(255) NOT NULL,
            context   TEXT,
            trace_id  VARCHAR(64),
            span_id   VARCHAR(32)
        )
        """;

    protected override DbConnection CreateConnection()
    {
        // Replace with your engine's DbConnection subclass
        var conn = new MyEngineConnection(connectionString);
        return conn;
    }
}
```

### 2. Add the NuGet package

```xml
<PackageReference Include="MyEngine.AdoNetDriver" Version="x.y.z" />
```

### 3. Register in `SinkFactory`

Add a branch to the `Create` switch and a private factory method:

```csharp
"myengine" => CreateMyEngineSink(config),

// ...

private static MyEngineSink CreateMyEngineSink(DiagnyxConfig config)
{
    var cs = config.Sink.MyEngine?.ConnectionString;
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException(
            "sink.myengine.connectionString is required. See docs/CONFIG.md.");
    return new MyEngineSink(cs);
}
```

### 4. Add the config model

In `Config/DiagnyxConfig.cs`, add a property on `SinkConfig`:

```csharp
public RdbmsSinkConfig? MyEngine { get; set; }
```

### 5. Document the config option

Add the new sink type to `docs/CONFIG.md` under both the `sink.type` enum table and a dedicated `sink.myengine` section.

---

## Shared Table Schema

All RDBMS sinks write to a table named `diagnyx_logs` with this logical column set:

| Column | Type (logical) | Notes |
|--------|----------------|-------|
| `id` | auto-increment integer | Added by the sink; not in the JSON log format |
| `timestamp` | timestamp / text | UTC; ISO 8601 string from the CLI |
| `level` | varchar | `debug`, `info`, `warn`, `error`, `fatal` |
| `message` | text | Unbounded |
| `source` | varchar | |
| `context` | text / json | JSON string; use native JSON type where the engine supports it |
| `trace_id` | varchar | Nullable; reserved for Phase 2 |
| `span_id` | varchar | Nullable; reserved for Phase 2 |

Engine-specific type mappings are defined in each sink's `CreateTableSql`. See `SCHEMA.md` for the full column reference.

## Adding a Non-RDBMS Sink

For sinks that are not relational databases (e.g. HTTP, message queue), implement `ISink` directly rather than inheriting `RdbmsSink`:

```csharp
internal sealed class HttpSink(string endpoint) : ISink
{
    public int Write(LogEntry entry)
    {
        try
        {
            // send entry to endpoint
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to write log entry: {ex.Message}");
            return 1;
        }
    }
}
```

Then follow steps 3–5 above to register it.
