# diagnyx (Go)

Thin Go client for the [Diagnyx](https://github.com/nachiketg/diagnyx) structured logging CLI.

`Logger` runs the `diagnyx` binary in a subprocess. The binary writes structured JSON log entries to whichever sinks `diagnyx.config.json` configures: file, SQLite, PostgreSQL, MySQL, SQL Server, OTLP, or Loki. The module has no dependencies outside the standard library and needs Go 1.22+.

## Installation

```bash
go get github.com/nachiketg/diagnyx/packages/go
```

The import path ends in `go`, but the package is named `diagnyx`:

```go
import "github.com/nachiketg/diagnyx/packages/go"
```

## Quick Start

```go
logger, err := diagnyx.NewLogger("my-api")
if err != nil {
    log.Fatal(err) // only if the source name is empty
}

logger.Info("App started")
logger.Warn("Cache miss", diagnyx.Fields{"key": "user:42", "ttl": 300})
logger.Error("Payment failed", diagnyx.Fields{"orderId": "ord-9921", "amount": 49.99})
```

Each call runs `diagnyx log` and returns an `error`: `nil` on success, otherwise one describing what failed. You can ignore it if you don't need it.

## The diagnyx binary

You don't need to install anything else on win-x64, linux-x64, or osx-arm64. Go modules can't run install steps, so the first log call that finds no binary downloads the one matching this module's version from [GitHub Releases](https://github.com/nachiketg/diagnyx/releases). It is saved to your user cache directory (for example `~/.cache/diagnyx/v1.1.0/diagnyx` on Linux or `~/Library/Caches/diagnyx/v1.1.0/diagnyx` on macOS), so this happens once per machine per version.

`Logger` looks for the binary once, on the first log call, in this order:

1. `WithBinaryPath(path)` passed to `NewLogger`.
2. The `DIAGNYX_PATH` environment variable.
3. A copy already downloaded to the user cache directory.
4. `diagnyx` on the system PATH (`diagnyx.exe` on Windows).
5. A fresh download into the user cache directory (win-x64, linux-x64, and osx-arm64 only).

To control the download:

- **Download ahead of time.** The first download can take a few seconds, so the first log call waits for it. To avoid that, call `diagnyx.Download(ctx)` at startup or in a build step, for example in a Docker image so containers start with the binary in place.
- **Turn downloads off.** Set `DIAGNYX_NO_DOWNLOAD=1` for air-gapped or locked-down environments. You then need to provide the binary through `DIAGNYX_PATH`, `WithBinaryPath`, or PATH.
- **Other platforms.** Install the CLI from the [releases page](https://github.com/nachiketg/diagnyx/releases) or [build it from source](https://github.com/nachiketg/diagnyx/tree/main/core), then point `DIAGNYX_PATH` at it.

## Failing gracefully

Logging never panics. If the binary can't be found or downloaded, or it fails to start, the call prints a warning to stderr, drops the entry, and returns an error. The host application keeps running:

```
diagnyx: warning: diagnyx binary not found. Install it from https://github.com/nachiketg/diagnyx/releases or set the DIAGNYX_PATH environment variable. Log entry dropped.
```

| Situation | Warning on stderr | Returned error |
|-----------|-------------------|----------------|
| No binary found or downloaded | `diagnyx binary not found` (plus `could not download the diagnyx binary: ...` on the first call, if a download was tried) | `ErrBinaryNotFound` (check with `errors.Is`) |
| Binary fails to start | `failed to invoke diagnyx CLI: ...` | wraps the start error |
| Fields can't be JSON-encoded (a channel, a function) | `fields are not JSON-serializable: ...` | wraps the encoding error |
| The CLI rejects the entry (for example, an invalid level) | none; the CLI prints its own error | `diagnyx: log exited with code N` |

## API Reference

### `NewLogger(source string, opts ...Option) (*Logger, error)`

Creates a logger that writes `source` into every entry's `source` field. It returns an error only if `source` is empty or whitespace. Option: `WithBinaryPath(path string)`. A `*Logger` is safe for concurrent use.

### Methods

| Method | Level |
|--------|-------|
| `Debug(msg string, fields ...Fields) error` | `debug` |
| `Info(msg string, fields ...Fields) error` | `info` |
| `Warn(msg string, fields ...Fields) error` | `warn` |
| `Error(msg string, fields ...Fields) error` | `error` |
| `Fatal(msg string, fields ...Fields) error` | `fatal` (unlike `log.Fatal`, it does **not** exit) |
| `DebugContext`, `InfoContext`, `WarnContext`, `ErrorContext`, `FatalContext` `(ctx context.Context, msg string, fields ...Fields) error` | as above, with the trace context from `ctx` |
| `Log(ctx context.Context, level, msg string, fields ...Fields) error` | any |

`Fields` is a `map[string]any`, written to the entry's `context` field. If you pass several, they are merged and later keys win. With no fields, `context` is `null`. This follows the same pattern as the standard library's `log/slog`: plain methods, plus `...Context` variants that take a `context.Context`.

### `Download(ctx context.Context) (string, error)`

Fetches this platform's binary for `Version` into the user cache directory, or returns at once if it's already there, and returns its path.

## Trace Context

To link log entries to a trace, attach the trace context to a `context.Context` with `diagnyx.WithTraceContext` and log with the `...Context` methods. Every call made with that context, or with any context derived from it, includes the trace and span IDs:

```go
func middleware(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        ctx := diagnyx.WithTraceContext(r.Context(), traceIDFrom(r), spanIDFrom(r))
        next.ServeHTTP(w, r.WithContext(ctx))
    })
}

// later, anywhere in the request:
logger.InfoContext(r.Context(), "processing order", diagnyx.Fields{"orderId": "ord-9921"})
```

The context only carries the trace IDs. If it is cancelled, the log write still goes ahead, so you can safely log at the end of a request.

If you use [OpenTelemetry](https://opentelemetry.io/docs/languages/go/), you can copy the active span's IDs across. This module doesn't depend on OpenTelemetry, so the snippet uses your own import of `go.opentelemetry.io/otel/trace`:

```go
if sc := trace.SpanContextFromContext(ctx); sc.IsValid() {
    ctx = diagnyx.WithTraceContext(ctx, sc.TraceID().String(), sc.SpanID().String())
}
```

A trace context that is invalid or missing leaves `traceId`/`spanId` as `null` on the log entry. It never causes an error.

## Configuration

`Logger` gets all sink and output configuration from `diagnyx.config.json`. See the [Config Reference](https://github.com/nachiketg/diagnyx/blob/main/docs/CONFIG.md) for all options.

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

## Development

```bash
cd packages/go
go vet ./...
go test -race ./...
```

The tests need nothing installed, because the test binary acts as a stand-in `diagnyx` CLI. `TestIntegration` runs against a real CLI when you set `DIAGNYX_PATH`, `DIAGNYX_CONFIG`, and `DIAGNYX_INTEGRATION_LOG` (the file-sink path); otherwise it is skipped. See `.github/workflows/ci.yml` for how CI runs it.
