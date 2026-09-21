# Diagnyx Configuration Reference

Diagnyx Core reads its configuration from a `diagnyx.config.json` file. This document describes every supported option, their defaults, and per-sink configuration.

---

## Config File Location

Diagnyx resolves config in this priority order (first found wins):

1. `./diagnyx.config.json` — project-local config (current working directory).
2. `~/.diagnyx/diagnyx.config.json` — user-level global config.
3. Built-in defaults (file sink, `~/.diagnyx/logs/diagnyx.log`).

Run `diagnyx init` to scaffold a default config file in the current directory.

---

## Full Schema

```json
{
  "$schema": "https://raw.githubusercontent.com/nachiketg/diagnyx/main/docs/config.schema.json",
  "sink": {
    "type": "file",
    "file": {
      "path": "~/.diagnyx/logs/diagnyx.log"
    },
    "sqlite": {
      "path": "~/.diagnyx/diagnyx.db"
    },
    "postgres": {
      "connectionString": "Host=localhost;Port=5432;Database=diagnyx;Username=user;Password=pass"
    },
    "mysql": {
      "connectionString": "Server=localhost;Port=3306;Database=diagnyx;Uid=user;Pwd=pass"
    },
    "mssql": {
      "connectionString": "Server=localhost;Database=diagnyx;User Id=user;Password=pass;TrustServerCertificate=True"
    },
    "otlp": {
      "endpoint": "http://localhost:4318",
      "maxRetries": 3
    },
    "loki": {
      "endpoint": "http://localhost:3100"
    }
  },
  "metrics": {
    "enabled": false,
    "path": "~/.diagnyx/metrics.db"
  },
  "defaults": {
    "source": "app"
  }
}
```

---

## Fields

### `sink.type`

**Type:** `string` (enum)  
**Required:** No  
**Default:** `"file"`

Selects the active sink. Exactly one sink is active at a time.

`file` and the database sinks can also be read back with [`diagnyx query`](QUERY.md); `otlp` and `loki` are export-only.

| Value | Description |
|-------|-------------|
| `"file"` | Write JSON Lines to a local file (default; zero setup). |
| `"sqlite"` | Write rows to a local SQLite `.db` file. |
| `"postgres"` | Write rows to a PostgreSQL database. |
| `"mysql"` | Write rows to a MySQL database. |
| `"mssql"` | Write rows to a Microsoft SQL Server database. |
| `"otlp"` | Export to any OTel-compatible collector via OTLP/HTTP (JSON). |
| `"loki"` | Push to a Grafana Loki-compatible endpoint. |

---

### `sink.file`

Only read when `sink.type` is `"file"`.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `path` | `string` | `~/.diagnyx/logs/diagnyx.log` | Path to the log file. `~` is expanded. Parent directories are created automatically if they don't exist. |

---

### `sink.sqlite`

**Status: implemented (v1)**

Only read when `sink.type` is `"sqlite"`.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `path` | `string` | `~/.diagnyx/diagnyx.db` | Path to the SQLite database file. Created automatically on first run. `~` is expanded. |

The `diagnyx_logs` table is created automatically if it does not exist. All columns use SQLite `TEXT` affinity, which is flexible and requires no driver configuration.

---

### `sink.postgres`

**Status: implemented (v1)**

Only read when `sink.type` is `"postgres"`.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `connectionString` | `string` | Yes | A standard [Npgsql connection string](https://www.npgsql.org/doc/connection-string-parameters.html). |

The `diagnyx_logs` table is created automatically on first run. `timestamp` and `context` are stored as `TEXT` for maximum compatibility. To query as typed values use SQL casts: `timestamp::timestamptz`, `context::jsonb`.

---

### `sink.mysql`

**Status: implemented (v1)**

Only read when `sink.type` is `"mysql"`.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `connectionString` | `string` | Yes | A standard [MySqlConnector connection string](https://mysqlconnector.net/connection-options/). |

The `diagnyx_logs` table is created automatically on first run using `InnoDB` / `utf8mb4`. `timestamp` and `context` are stored as `VARCHAR(50)` and `TEXT` respectively, so plain string parameters from ADO.NET bind without driver type coercion.

---

### `sink.mssql`

**Status: implemented (v1)**

Only read when `sink.type` is `"mssql"`.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `connectionString` | `string` | Yes | A standard [Microsoft.Data.SqlClient connection string](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.connectionstring). |

The `diagnyx_logs` table is created automatically on first run. `timestamp` is stored as `NVARCHAR(50)` (ISO 8601 string) for consistent TEXT storage across all engines. Include `TrustServerCertificate=True` in the connection string when connecting to a local or self-signed instance.

---

### `sink.otlp`

**Status: implemented (v1)**

Only read when `sink.type` is `"otlp"`.

| Key | Type | Required | Default | Description |
|-----|------|----------|---------|-------------|
| `endpoint` | `string` | Yes | — | Base URL of an OTel-compatible collector, e.g. `http://localhost:4318`. `/v1/logs` is appended automatically unless the URL already ends with it. |
| `maxRetries` | `number` | No | `3` | Number of retries after an initial failed export, so the default allows up to 4 attempts total. Negative values are treated as `0`. |

Each `diagnyx log` call sends one `POST` request with an OTLP/HTTP JSON body (`Content-Type: application/json`) containing exactly one `LogRecord`, using the field mapping documented in [`docs/OTEL_MAPPING.md`](OTEL_MAPPING.md). A 10-second request timeout applies per attempt.

**Retry and backoff:** a failed export is retried with exponential backoff (500ms, 1s, 2s, ..., capped at 8s between attempts) only when the failure looks transient:

- Connection errors, DNS failures, and timeouts — always retried.
- HTTP `429`, `502`, `503`, `504` — retried.
- Any other non-2xx response (e.g. `400`, `401`, `404`) — not retried; the request itself is presumed unfixable by retrying, so the command fails immediately.

Each retry prints a `warning:` line to stderr before sleeping. Once `maxRetries` is exhausted (or a non-retryable failure occurs), a final `error:` line is printed and the command exits `1` — the log entry is never silently discarded, but it is also not persisted or re-queued after the process exits.

---

### `sink.loki`

**Status: implemented (v1)**

Only read when `sink.type` is `"loki"`.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `endpoint` | `string` | Yes | Base URL of a Grafana Loki-compatible endpoint, e.g. `http://localhost:3100`. `/loki/api/v1/push` is appended automatically unless the URL already ends with it. |

Each `diagnyx log` call sends one `POST` request to Loki's push API with a single stream containing one entry:

```json
{
  "streams": [
    {
      "stream": { "service_name": "my-api", "level": "info" },
      "values": [["1735128896789000000", "{\"timestamp\":\"...\",\"level\":\"info\",\"message\":\"...\", ...}"]]
    }
  ]
}
```

**Labels vs. line:** only `source` (as `service_name`) and `level` become Loki labels. Loki indexes streams by label, and label values need to stay low-cardinality — putting `message` or `traceId` in a label would create a new stream per log entry and degrade query performance, so they don't belong there. The log line itself is the full canonical JSON entry from [`docs/SCHEMA.md`](SCHEMA.md) (`timestamp`, `level`, `message`, `source`, `context`, `traceId`, `spanId`), so every field is still queryable in Grafana Explore via LogQL's `| json` parser, e.g. `{service_name="my-api"} | json | traceId="4bf92f3577b34da6a3ce929d0e0e4736"`.

A 10-second request timeout applies. If the request fails (connection error, timeout, or a non-2xx response — Loki returns `204 No Content` on success), the error is printed to stderr and the command exits `1` — the log entry is not retried or buffered.

See [`dashboards/diagnyx-logs.json`](../dashboards/diagnyx-logs.json) for a starter Grafana dashboard that queries this sink's labels.

---

### `metrics`

**Status: implemented (v1)**

Independent of `sink.type` — metrics are tracked (if enabled) no matter which sink is active.

| Key | Type | Required | Default | Description |
|-----|------|----------|---------|-------------|
| `enabled` | `boolean` | No | `false` | Opt-in. When `false` (the default), `diagnyx log` does no metrics-related work at all — no file is created. |
| `path` | `string` | No | `~/.diagnyx/metrics.db` | Path to a local SQLite counter store. `~` is expanded. Created automatically on first write. |

When enabled, every `diagnyx log` call increments a `(level, source)` counter in the store — a side effect independent of whether the sink write itself succeeds. A counter-store failure is printed as a `warning:` to stderr; it never changes the command's exit code.

```json
{
  "metrics": {
    "enabled": true,
    "path": "~/.diagnyx/metrics.db"
  }
}
```

### `diagnyx metrics serve`

Serves the counters as a Prometheus scrape target:

```bash
diagnyx metrics serve [--port <port>]
```

| Flag | Default | Description |
|------|---------|-------------|
| `--port` | `9464` | Port to listen on (`9464` is the IANA-registered default for Prometheus exporters). Must be `1`-`65535`. |

Requires `metrics.enabled: true` in config — if metrics are disabled, the command fails immediately with a clear error rather than serving an endpoint that can never have data. The server binds to `localhost` only and serves exactly one route:

- `GET /metrics` — current counts in [Prometheus text exposition format](https://prometheus.io/docs/instrumenting/exposition_formats/):

  ```
  # HELP diagnyx_log_entries_total Total number of log entries processed by Diagnyx, by level and source.
  # TYPE diagnyx_log_entries_total counter
  diagnyx_log_entries_total{level="info",source="my-api"} 42
  diagnyx_log_entries_total{level="error",source="my-api"} 3
  ```

Any other path returns `404`. The server runs until stopped (Ctrl+C, or the process is otherwise terminated) — it's meant to run under a process supervisor (systemd, a container, etc.) alongside your application, not to be started per log call.

---

### `defaults.source`

**Type:** `string`  
**Default:** `"app"`

The value used for the `source` field when `--source` is not passed to `diagnyx log`. Language wrappers can override this per-instance.

---

## Example Configs

### Minimal (all defaults)

```json
{
  "sink": {
    "type": "file"
  }
}
```

### Custom file path

```json
{
  "sink": {
    "type": "file",
    "file": {
      "path": "./logs/app.log"
    }
  },
  "defaults": {
    "source": "my-api"
  }
}
```

### SQLite (local queryable store)

```json
{
  "sink": {
    "type": "sqlite",
    "sqlite": {
      "path": "./logs/diagnyx.db"
    }
  },
  "defaults": {
    "source": "my-api"
  }
}
```

### PostgreSQL

```json
{
  "sink": {
    "type": "postgres",
    "postgres": {
      "connectionString": "Host=localhost;Port=5432;Database=diagnyx;Username=diagnyx_user;Password=s3cr3t"
    }
  }
}
```

### MySQL

```json
{
  "sink": {
    "type": "mysql",
    "mysql": {
      "connectionString": "Server=localhost;Port=3306;Database=diagnyx;Uid=diagnyx_user;Pwd=s3cr3t;"
    }
  }
}
```

### Microsoft SQL Server

```json
{
  "sink": {
    "type": "mssql",
    "mssql": {
      "connectionString": "Server=localhost;Database=diagnyx;User Id=diagnyx_user;Password=s3cr3t;TrustServerCertificate=True"
    }
  }
}
```

### OTLP (any OTel-compatible collector)

```json
{
  "sink": {
    "type": "otlp",
    "otlp": {
      "endpoint": "http://localhost:4318",
      "maxRetries": 3
    }
  }
}
```

### Loki / Grafana

```json
{
  "sink": {
    "type": "loki",
    "loki": {
      "endpoint": "http://localhost:3100"
    }
  }
}
```

### With Prometheus metrics enabled

`metrics` is independent of `sink` — it works alongside any sink:

```json
{
  "sink": {
    "type": "file"
  },
  "metrics": {
    "enabled": true
  }
}
```

Run `diagnyx metrics serve` alongside your application to expose the counts at `http://localhost:9464/metrics`.

---

## Switching Sinks

To switch from the file sink to SQLite — for example to gain SQL queryability — change only the config file. No code changes are needed in your .NET or Node.js application:

```diff
 {
   "sink": {
-    "type": "file",
-    "file": { "path": "./logs/app.log" }
+    "type": "sqlite",
+    "sqlite": { "path": "./logs/diagnyx.db" }
   }
 }
```

The same applies to `otlp`: point an existing app at a collector, or move off it, with a config change alone.

```diff
 {
   "sink": {
-    "type": "file",
-    "file": { "path": "./logs/app.log" }
+    "type": "otlp",
+    "otlp": { "endpoint": "http://localhost:4318" }
   }
 }
```

---

## Environment Variable Override

`DIAGNYX_CONFIG` — if set, overrides the config file path entirely.

```bash
DIAGNYX_CONFIG=/etc/diagnyx/config.json diagnyx log --level info --message "hello"
```

---

## Notes

- The config file itself should **not** be committed to version control if it contains credentials (connection strings). Add `diagnyx.config.json` to `.gitignore` and use environment-specific configs or `DIAGNYX_CONFIG` in CI/CD.
- Connection strings for RDBMS sinks follow the conventions of their respective .NET ADO.NET providers; the same format works whether Diagnyx Core is running on Windows, Linux, or macOS.
