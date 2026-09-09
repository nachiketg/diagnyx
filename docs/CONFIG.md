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
    }
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

| Value | Description |
|-------|-------------|
| `"file"` | Write JSON Lines to a local file (default; zero setup). |
| `"sqlite"` | Write rows to a local SQLite `.db` file. |
| `"postgres"` | Write rows to a PostgreSQL database. |
| `"mysql"` | Write rows to a MySQL database. |
| `"mssql"` | Write rows to a Microsoft SQL Server database. |
| `"otlp"` | Export to any OTel-compatible collector via OTLP/HTTP (JSON). |

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
