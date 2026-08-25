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

**Status: coming soon (DX-013)**

Only read when `sink.type` is `"mysql"`.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `connectionString` | `string` | Yes | A standard [MySqlConnector connection string](https://mysqlconnector.net/connection-options/). |

---

### `sink.mssql`

**Status: coming soon (DX-014)**

Only read when `sink.type` is `"mssql"`.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `connectionString` | `string` | Yes | A standard [Microsoft.Data.SqlClient connection string](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.connectionstring). |

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
