# diagnyx-dotnet

Thin .NET client for the [Diagnyx](https://github.com/nachiketg/diagnyx) structured logging CLI.

`DiagnyxLogger` calls the `diagnyx` binary in a subprocess, writing structured JSON log entries to any sink configured in `diagnyx.config.json` — file, SQLite, PostgreSQL, MySQL, or SQL Server.

## Prerequisites

None on win-x64, linux-x64, or osx-arm64 — the package bundles the matching `diagnyx` binary for those platforms, so `dotnet add package diagnyx-dotnet` is enough on its own.

On any other platform, install the Diagnyx CLI from the [releases page](https://github.com/nachiketg/diagnyx/releases), or build from source, then point `DiagnyxLogger` at it via the `binaryPath` constructor argument or the `DIAGNYX_PATH` environment variable (see [Binary Discovery](#binary-discovery)):

```bash
git clone https://github.com/nachiketg/diagnyx
cd diagnyx/src/core
dotnet publish -c Release -r linux-x64 -o /usr/local/bin
```

## Installation

```bash
dotnet add package diagnyx-dotnet
```

## Quick Start

```csharp
using Diagnyx;

var logger = new DiagnyxLogger(source: "my-api");

logger.Info("App started");
logger.Warn("Cache miss", new { key = "user:42", ttl = 300 });
logger.Error("Payment failed", new { orderId = "ord-9921", amount = 49.99 });
```

Each call invokes `diagnyx log` and returns the exit code (`0` = success, `1` = failure).

## Binary Discovery

`DiagnyxLogger` locates the CLI in this order:

1. The `binaryPath` constructor argument.
2. The `DIAGNYX_PATH` environment variable.
3. The binary bundled inside the NuGet package (win-x64, linux-x64, osx-arm64 only).
4. `diagnyx` (or `diagnyx.exe` on Windows) on the system PATH.

```csharp
// Explicit path
var logger = new DiagnyxLogger("my-api", binaryPath: "/opt/diagnyx/diagnyx");

// Via environment variable (e.g. in CI)
// DIAGNYX_PATH=/opt/diagnyx/diagnyx
var logger = new DiagnyxLogger("my-api");
```

If the binary cannot be found or fails to run, `DiagnyxLogger` does not throw: it prints a warning to `Console.Error` and the call returns `1`, so a missing or broken CLI never crashes the host application.

## API Reference

### `DiagnyxLogger(string source, string? binaryPath = null)`

| Parameter | Description |
|-----------|-------------|
| `source` | Application or service name written to the `source` field of every log entry. |
| `binaryPath` | Optional explicit path to the diagnyx binary. |

### Methods

| Method | Level |
|--------|-------|
| `Debug(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)` | `debug` |
| `Info(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)` | `info` |
| `Warn(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)` | `warn` |
| `Error(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)` | `error` |
| `Fatal(string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)` | `fatal` |
| `Log(string level, string message, object? context = null, (string TraceId, string SpanId)? traceContext = null)` | any |

The `context` parameter accepts any object (serialized with `System.Text.Json`) or a pre-serialized JSON object string.

## Trace Context

Every log entry carries `traceId`/`spanId` when a trace is active, with no extra wiring:

```csharp
using System.Diagnostics;

using var activity = new Activity("checkout").Start();
logger.Info("processing order"); // traceId/spanId come from Activity.Current
```

`DiagnyxLogger` reads `Activity.Current` (the ambient, `AsyncLocal`-based trace context that .NET and OpenTelemetry instrumentation already populate) automatically. To correlate with a trace you're tracking yourself instead, pass it explicitly — it takes priority over `Activity.Current`:

```csharp
logger.Info("processing order", traceContext: ("4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7"));
```

An invalid or missing trace context results in `traceId`/`spanId` being `null`, never an error.

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
