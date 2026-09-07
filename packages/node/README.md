# diagnyx-node

Thin Node.js client for the [Diagnyx](https://github.com/nachiketg/diagnyx) structured logging CLI.

`DiagnyxLogger` calls the `diagnyx` binary in a subprocess, writing structured JSON log entries to any sink configured in `diagnyx.config.json` — file, SQLite, PostgreSQL, MySQL, or SQL Server.

## Prerequisites

None on win-x64, linux-x64, or osx-arm64 — a `postinstall` script downloads the matching `diagnyx` binary from GitHub Releases automatically, so `npm install diagnyx-node` is enough on its own. A failed or skipped download (offline install, unsupported platform, no release published yet) is only a warning; it never fails `npm install`.

On any other platform, or if the download didn't happen, install the Diagnyx CLI from the [releases page](https://github.com/nachiketg/diagnyx/releases), or build from source, then point `DiagnyxLogger` at it via the `binaryPath` constructor argument or the `DIAGNYX_PATH` environment variable (see [Binary Discovery](#binary-discovery)):

```bash
git clone https://github.com/nachiketg/diagnyx
cd diagnyx/src/core
dotnet publish -c Release -r linux-x64 -o /usr/local/bin
```

## Installation

```bash
npm install diagnyx-node
```

## Quick Start

```javascript
const { DiagnyxLogger } = require('diagnyx-node');

const logger = new DiagnyxLogger('my-api');

logger.info('App started');
logger.warn('Cache miss', { key: 'user:42', ttl: 300 });
logger.error('Payment failed', { orderId: 'ord-9921', amount: 49.99 });
```

Each call invokes `diagnyx log` and returns the exit code (`0` = success, `1` = failure).

## Binary Discovery

`DiagnyxLogger` locates the CLI in this order:

1. The `binaryPath` constructor argument (second parameter).
2. The `DIAGNYX_PATH` environment variable.
3. The binary downloaded into this package's `bin/` folder by `postinstall` (win-x64, linux-x64, osx-arm64 only).
4. `diagnyx` (or `diagnyx.exe` on Windows) on the system PATH.

If none of these resolve to a binary, or the binary fails to run, `DiagnyxLogger` does not throw: it prints a warning to the console and the call returns `1`, so a missing or broken CLI never crashes the host application.

```javascript
// Explicit path
const logger = new DiagnyxLogger('my-api', '/opt/diagnyx/diagnyx');

// Via environment variable (e.g. in CI)
// DIAGNYX_PATH=/opt/diagnyx/diagnyx
const logger = new DiagnyxLogger('my-api');
```

## API Reference

### `new DiagnyxLogger(source, binaryPath?)`

| Parameter | Type | Description |
|-----------|------|-------------|
| `source` | `string` | Application or service name written to the `source` field of every log entry. |
| `binaryPath` | `string` (optional) | Explicit path to the diagnyx binary. |

### Methods

| Method | Level |
|--------|-------|
| `debug(message, context?, traceContext?)` | `debug` |
| `info(message, context?, traceContext?)` | `info` |
| `warn(message, context?, traceContext?)` | `warn` |
| `error(message, context?, traceContext?)` | `error` |
| `fatal(message, context?, traceContext?)` | `fatal` |
| `log(level, message, context?, traceContext?)` | any |

`message` is a string. `context` accepts any JSON-serializable object or a pre-serialized JSON object string. All methods return the CLI exit code (`0` = success, `1` = failure).

## Trace Context

Pass `{ traceId, spanId }` explicitly to correlate a specific entry with a trace:

```javascript
logger.info('processing order', { orderId: 'ord-9921' }, { traceId: '4bf92f3577b34da6a3ce929d0e0e4736', spanId: '00f067aa0ba902b7' });
```

Or set it as the ambient context for a whole async scope (e.g. one request) with `DiagnyxLogger.withTraceContext`, backed by Node's built-in `AsyncLocalStorage` — every log call made inside it, including through further `await`s, picks it up automatically:

```javascript
app.use((req, res, next) => {
  DiagnyxLogger.withTraceContext({ traceId: req.traceId, spanId: req.spanId }, next);
});
```

An explicit `traceContext` argument takes priority over the ambient one. An invalid or missing trace context results in `traceId`/`spanId` being `null` on the log entry, never an error.

## Configuration

`DiagnyxLogger` inherits all sink and output configuration from `diagnyx.config.json`. See the [Config Reference](https://github.com/nachiketg/diagnyx/blob/main/docs/CONFIG.md) for all options.

```json
{
  "sink": {
    "type": "postgres",
    "postgres": {
      "connectionString": "Host=localhost;Database=myapp;Username=app;Password=secret"
    }
  },
  "defaults": {
    "source": "my-api"
  }
}
```
