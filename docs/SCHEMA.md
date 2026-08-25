# Diagnyx Log Entry Schema

This document defines the JSON log entry schema — the shared contract between Diagnyx Core, all language wrappers, and all downstream consumers (sinks, observability exporters, AI analysis).

**Format:** [JSON Lines](https://jsonlines.org/) (NDJSON) — one JSON object per line, UTF-8 encoded, `\n` terminated.

---

## Fields

### Required fields

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | `string` (ISO 8601, UTC) | When the event occurred. Always in UTC. Format: `YYYY-MM-DDTHH:mm:ss.sssZ`. |
| `level` | `string` (enum) | Severity level. Must be one of: `debug`, `info`, `warn`, `error`, `fatal`. |
| `message` | `string` | Human-readable description of the event. Must be non-empty. |
| `source` | `string` | The application, service, or component that emitted this entry. Used to identify origin in multi-service setups. |

### Optional fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `context` | `object` \| `null` | `null` | Arbitrary structured metadata as a JSON object. Values may be any JSON type. Not nested arrays of arrays. |
| `traceId` | `string` \| `null` | `null` | Distributed trace ID, aligned with [OpenTelemetry W3C Trace Context](https://www.w3.org/TR/trace-context/). Reserved for Phase 2; populate now if available. |
| `spanId` | `string` \| `null` | `null` | Span ID within the trace. Reserved for Phase 2. |

---

## Level Semantics

| Level | Meaning |
|-------|---------|
| `debug` | Detailed diagnostic information; typically disabled in production. |
| `info` | Normal operational events (startup, request completed, config loaded). |
| `warn` | Something unexpected happened but the app continued; investigate when convenient. |
| `error` | A failure occurred; the operation could not complete. Requires attention. |
| `fatal` | A critical failure that will cause (or has caused) the process to terminate. |

---

## Full Example

```json
{
  "timestamp": "2025-08-25T12:34:56.789Z",
  "level": "error",
  "message": "Payment processing failed",
  "source": "payment-service",
  "context": {
    "orderId": "ord-9921",
    "amount": 49.99,
    "currency": "USD",
    "retryCount": 3
  },
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "00f067aa0ba902b7"
}
```

Minimal valid entry (all optional fields omitted or null):

```json
{"timestamp":"2025-08-25T12:34:56.789Z","level":"info","message":"App started","source":"my-app","context":null,"traceId":null,"spanId":null}
```

---

## Schema Rules

1. Each line in a `.log` file is a complete, self-contained JSON object.
2. Lines are separated by `\n`. Trailing newline after the last entry is allowed.
3. `timestamp` is always UTC; timezone offset (`+00:00`) is normalized to `Z`.
4. `level` values are always lowercase.
5. `context` must be a flat-or-nested JSON **object** (not an array or primitive). If no context is provided, the field is `null`, not omitted — all seven fields are always present in every entry written by Diagnyx Core.
6. `traceId` and `spanId` follow the [W3C TraceContext hex string format](https://www.w3.org/TR/trace-context/#trace-id) when populated.
7. String field values must not contain embedded newlines (the log file format is line-delimited).

---

## RDBMS Column Mapping

When writing to a relational database sink, the same seven fields map to columns in the `diagnyx_logs` table:

| Column | Logical type | Notes |
|--------|-------------|-------|
| `id` | auto-increment integer | Added by the sink; not present in the JSON log format. |
| `timestamp` | ISO 8601 text string | Stored as a plain string — no TIMESTAMP/DATETIME type — so ADO.NET binds without driver type coercion. |
| `level` | short string | `debug`, `info`, `warn`, `error`, `fatal`. |
| `message` | unbounded text | |
| `source` | bounded string | |
| `context` | JSON-encoded text string | Stored as a plain string regardless of engine. Use a SQL cast when querying as a native JSON type (see per-engine notes below). |
| `trace_id` | short string | Nullable. |
| `span_id` | short string | Nullable. |

### Per-engine column types

All engines use text-based storage for every column to ensure plain `string` parameters from ADO.NET bind without type coercion. The actual SQL types differ per engine but are logically equivalent:

| Column | SQLite | PostgreSQL | MySQL | SQL Server |
|--------|--------|------------|-------|------------|
| `id` | `INTEGER PRIMARY KEY AUTOINCREMENT` | `BIGSERIAL PRIMARY KEY` | `INT NOT NULL AUTO_INCREMENT PRIMARY KEY` | `INT IDENTITY(1,1) PRIMARY KEY` |
| `timestamp` | `TEXT` | `TEXT` | `VARCHAR(50)` | `NVARCHAR(50)` |
| `level` | `TEXT` | `VARCHAR(10)` | `VARCHAR(10)` | `NVARCHAR(10)` |
| `message` | `TEXT` | `TEXT` | `TEXT` | `NVARCHAR(MAX)` |
| `source` | `TEXT` | `VARCHAR(255)` | `VARCHAR(255)` | `NVARCHAR(255)` |
| `context` | `TEXT` | `TEXT` | `TEXT` | `NVARCHAR(MAX)` |
| `trace_id` | `TEXT` | `VARCHAR(64)` | `VARCHAR(64)` | `NVARCHAR(64)` |
| `span_id` | `TEXT` | `VARCHAR(32)` | `VARCHAR(32)` | `NVARCHAR(32)` |

**Querying typed values (PostgreSQL):** cast at query time — `timestamp::timestamptz`, `context::jsonb`.

Table name is `diagnyx_logs` — identical across all engines.

---

## Versioning

This schema is **stable from v1**. Future changes will be **additive only** (new optional fields) to avoid breaking existing consumers, sinks, and Phase 2/3 tooling built against this contract.
