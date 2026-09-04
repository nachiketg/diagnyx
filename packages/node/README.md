# diagnyx-node

Thin Node.js client for the [Diagnyx](https://github.com/nachiketg/diagnyx) structured logging CLI.

`DiagnyxLogger` calls the `diagnyx` binary in a subprocess, writing structured JSON log entries to any sink configured in `diagnyx.config.json` — file, SQLite, PostgreSQL, MySQL, or SQL Server.

## Prerequisites

Install the Diagnyx CLI for your platform from the [releases page](https://github.com/nachiketg/diagnyx/releases), or build from source:

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
3. `diagnyx` (or `diagnyx.exe` on Windows) on the system PATH.

```javascript
// Explicit path
const logger = new DiagnyxLogger('my-api', '/opt/diagnyx/diagnyx');

// Via environment variable (e.g. in CI)
// DIAGNYX_PATH=/opt/diagnyx/diagnyx
const logger = new DiagnyxLogger('my-api');
```

If the binary cannot be found or fails to run, `DiagnyxLogger` does not throw: it prints a warning to the console and the call returns `1`, so a missing or broken CLI never crashes the host application.

## API Reference

### `new DiagnyxLogger(source, binaryPath?)`

| Parameter | Type | Description |
|-----------|------|-------------|
| `source` | `string` | Application or service name written to the `source` field of every log entry. |
| `binaryPath` | `string` (optional) | Explicit path to the diagnyx binary. |

### Methods

| Method | Level |
|--------|-------|
| `debug(message, context?)` | `debug` |
| `info(message, context?)` | `info` |
| `warn(message, context?)` | `warn` |
| `error(message, context?)` | `error` |
| `fatal(message, context?)` | `fatal` |
| `log(level, message, context?)` | any |

`message` is a string. `context` accepts any JSON-serializable object or a pre-serialized JSON object string. All methods return the CLI exit code (`0` = success, `1` = failure).

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
