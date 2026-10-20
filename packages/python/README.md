# diagnyx (Python)

Thin Python client for the [Diagnyx](https://github.com/nachiketg/diagnyx) structured logging CLI.

`DiagnyxLogger` calls the `diagnyx` binary in a subprocess. The binary writes structured JSON log entries to whichever sinks `diagnyx.config.json` configures: file, SQLite, PostgreSQL, MySQL, SQL Server, OTLP, or Loki. The package has no runtime dependencies and supports Python 3.9+.

## Prerequisites

You don't need to install anything else on win-x64, linux-x64 (glibc 2.34+), or osx-arm64. PyPI publishes a separate wheel for each of these platforms, and each wheel bundles the matching `diagnyx` binary, so `pip install diagnyx` is all you need.

On other platforms (or with `pip install --no-binary diagnyx`), pip installs the pure wheel or the sdist instead. Neither one includes a binary. Install the Diagnyx CLI from the [releases page](https://github.com/nachiketg/diagnyx/releases) or build it from source. Then point `DiagnyxLogger` at it with the `binary_path` constructor argument or the `DIAGNYX_PATH` environment variable (see [Binary Discovery](#binary-discovery)):

```bash
git clone https://github.com/nachiketg/diagnyx
cd diagnyx/src/core
dotnet publish -c Release -r linux-x64 -o /usr/local/bin
```

## Installation

```bash
pip install diagnyx
```

## Quick Start

```python
from diagnyx import DiagnyxLogger

logger = DiagnyxLogger("my-api")

logger.info("App started")
logger.warn("Cache miss", {"key": "user:42", "ttl": 300})
logger.error("Payment failed", {"orderId": "ord-9921", "amount": 49.99})
```

Each call runs `diagnyx log` and returns the exit code (`0` = success, `1` = failure).

## Binary Discovery

`DiagnyxLogger` looks for the CLI once, when it is constructed, in this order:

1. The `binary_path` constructor argument.
2. The `DIAGNYX_PATH` environment variable.
3. The binary bundled in this package's platform wheel (`diagnyx/bin/`, only on win-x64, linux-x64, and osx-arm64).
4. `diagnyx` on the system PATH (`diagnyx.exe` on Windows).

If none of these finds a binary, or the binary fails to run, `DiagnyxLogger` does not raise an exception. It prints a warning to stderr and the call returns `1`, so a missing or broken CLI never crashes the host application:

```
diagnyx: warning: diagnyx binary not found. Install it from https://github.com/nachiketg/diagnyx/releases or set the DIAGNYX_PATH environment variable. Log entry dropped.
```

```python
# Explicit path (str or pathlib.Path)
logger = DiagnyxLogger("my-api", "/opt/diagnyx/diagnyx")

# Via environment variable (e.g. in CI)
# DIAGNYX_PATH=/opt/diagnyx/diagnyx
logger = DiagnyxLogger("my-api")
```

## API Reference

### `DiagnyxLogger(source, binary_path=None)`

| Parameter | Type | Description |
|-----------|------|-------------|
| `source` | `str` | Application or service name written to the `source` field of every log entry. Raises `ValueError` if empty. |
| `binary_path` | `str` or `os.PathLike` (optional) | Explicit path to the diagnyx binary. |

### Methods

| Method | Level |
|--------|-------|
| `debug(message, context=None, trace_context=None)` | `debug` |
| `info(message, context=None, trace_context=None)` | `info` |
| `warn(message, context=None, trace_context=None)` | `warn` |
| `warning(...)` | alias of `warn`, matching the standard library's `logging` |
| `error(message, context=None, trace_context=None)` | `error` |
| `fatal(message, context=None, trace_context=None)` | `fatal` |
| `log(level, message, context=None, trace_context=None)` | any |

`message` is converted with `str()`, so passing an exception object works. `context` accepts a JSON-serializable object (usually a `dict`) or a JSON object string that is already serialized. Values with an `isoformat()` method (`datetime`, `date`, `time`) are serialized that way, and other values that aren't JSON types (such as `UUID`, `Decimal`, or `Path`) are serialized with `str()`. If the context can't be serialized at all, for example because it contains a circular reference, the logger prints a warning and the entry is dropped, without raising. Every method returns the CLI exit code (`0` = success, `1` = failure).

## Trace Context

To link one log entry to a trace, pass a `(trace_id, span_id)` pair explicitly. `TraceContext` is a named tuple, and a plain tuple works too:

```python
from diagnyx import DiagnyxLogger, TraceContext

logger.info(
    "processing order",
    {"orderId": "ord-9921"},
    TraceContext("4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7"),
)
```

You can also set an ambient trace context for a whole scope, such as one request, with the `DiagnyxLogger.trace_context` context manager. It is built on the standard library's `contextvars`. Every log call inside the scope picks up the trace context automatically, including calls after an `await` and calls in asyncio tasks created inside the scope:

```python
async def handle(request):
    with DiagnyxLogger.trace_context(request.trace_id, request.span_id):
        await process(request)  # every logger call in here is correlated
```

If you use [OpenTelemetry](https://opentelemetry.io/docs/languages/python/), you don't need to do anything else. When `opentelemetry-api` is installed and a span is active, its trace and span IDs are used automatically. Diagnyx doesn't depend on OpenTelemetry. It only reads the current span when the package is available.

When more than one source is present, they are used in this order:

1. The explicit `trace_context` argument.
2. The ambient `DiagnyxLogger.trace_context(...)` scope.
3. The active OpenTelemetry span.

A trace context that is invalid or missing leaves `traceId`/`spanId` as `null` on the log entry. It never causes an error.

## Configuration

`DiagnyxLogger` gets all sink and output configuration from `diagnyx.config.json`. See the [Config Reference](https://github.com/nachiketg/diagnyx/blob/main/docs/CONFIG.md) for all options.

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

## Building the package

The `scripts/build-dists.sh` script builds the sdist, the pure wheel, and the three platform wheels from a directory of release binaries. The release workflow uses it, and you can run it locally (`pip install build` first):

```bash
packages/python/scripts/build-dists.sh <dir-with-release-binaries> dist-python
```

See [docs/RELEASING.md](https://github.com/nachiketg/diagnyx/blob/main/docs/RELEASING.md) for details.
