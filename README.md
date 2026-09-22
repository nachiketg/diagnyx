# Diagnyx

A lightweight, language-agnostic structured logging tool that grows into a full observability and AI-assisted root-cause-analysis platform — without requiring a rewrite at each stage.

## Vision

One small core program does the real work. Every language gets a thin wrapper package that talks to that core using a common, documented convention. Apps in any language can produce logs that Diagnyx understands.

## Repository Structure

```
diagnyx/
├── core/                   # Diagnyx Core CLI (.NET, published as Native AOT binary)
├── packages/
│   ├── dotnet/             # diagnyx-dotnet — NuGet wrapper for .NET apps
│   └── node/               # diagnyx-node — npm wrapper for Node.js apps
├── docs/
│   ├── SCHEMA.md           # Log entry JSON schema (the contract)
│   ├── CONFIG.md           # Config file format reference
│   ├── EXTENDING_SINKS.md  # How to add a new sink engine
│   ├── QUERY.md            # diagnyx query reference
│   └── OTEL_MAPPING.md     # Field mapping onto the OpenTelemetry Logs Data Model
├── dashboards/             # Starter Grafana dashboards for the loki sink
├── CONTRIBUTING.md
└── LICENSE
```

## Quick Start

> **Note:** v1 is under active development. Releases are published to [GitHub Releases](https://github.com/nachiketg/diagnyx/releases), NuGet, and npm as they're cut — the install commands below work today.

### .NET

```bash
dotnet add package diagnyx-dotnet
```

```csharp
using Diagnyx;

var logger = new DiagnyxLogger("my-service");
logger.Info("App started", new { userId = 42 });
logger.Error("Something went wrong", new { errorCode = 500 });
```

No separate CLI install needed on win-x64, linux-x64, or osx-arm64 — the package bundles the matching `diagnyx` binary. See [packages/dotnet/README.md](packages/dotnet/README.md) for details.

### Node.js

```bash
npm install diagnyx-node
```

```js
const { DiagnyxLogger } = require('diagnyx-node');

const logger = new DiagnyxLogger('my-service');
logger.info('App started', { userId: 42 });
logger.error('Something went wrong', { errorCode: 500 });
```

No separate CLI install needed on win-x64, linux-x64, or osx-arm64 — a `postinstall` step fetches the matching `diagnyx` binary automatically. See [packages/node/README.md](packages/node/README.md) for details.

## Log Format

All log entries are written as [JSON Lines](https://jsonlines.org/) (one JSON object per line). See [docs/SCHEMA.md](docs/SCHEMA.md) for the full field reference, and [docs/OTEL_MAPPING.md](docs/OTEL_MAPPING.md) for how these fields map onto the OpenTelemetry Logs Data Model.

Example entry:

```json
{"timestamp":"2025-08-25T12:00:00.000Z","level":"info","message":"App started","source":"my-service","context":{"userId":42},"traceId":null,"spanId":null}
```

## Configuration

Diagnyx is configured via a `diagnyx.config.json` file in your project root or `~/.diagnyx/`. Run `diagnyx init` to scaffold a default config. See [docs/CONFIG.md](docs/CONFIG.md) for all options — every Phase 2 addition is optional, so a v1 config file keeps working [unmodified](docs/CONFIG.md#backward-compatibility). Need more than one sink at once — a local file plus an OTLP export, say? [`sink.types`](docs/CONFIG.md#sinktypes-fan-out) fans out to all of them, and a failure in one never blocks the others.

## Querying Logs

Search what you've logged — by time range, level, source, and text — without writing SQL or grepping files. It works against the file sink and every database sink:

```bash
diagnyx query --since 1h --level error --source my-api --contains timeout
```

See [docs/QUERY.md](docs/QUERY.md) for all flags and matching rules.

## Architecture

```
                 +----------------------+        +-----------------------------+
  .NET app  ---->|                      |        |  File sink (default)        |
                 |  Diagnyx Core (CLI)  |------->|  JSON Lines log file        |
  Node.js app -->|                      |   |    +-----------------------------+
                 +----------------------+   |
        ^                    ^              |    +-----------------------------+
        |                    |              +--->|  RDBMS sink (optional)      |
  diagnyx-dotnet        diagnyx-node        |    |  SQLite / Postgres /        |
  (NuGet wrapper)      (npm wrapper)        |    |  MySQL / MSSQL              |
                                             |    +-----------------------------+
                                             |
                                             |    +-----------------------------+
                                             +--->|  OTLP sink (optional)       |
                                             |    |  any OTel-compatible        |
                                             |    |  collector, via HTTP/JSON   |
                                             |    +-----------------------------+
                                             |
                                             |    +-----------------------------+
                                             +--->|  Loki sink (optional)       |
                                                  |  push to Grafana Loki,      |
                                                  |  queryable in Explore       |
                                                  +-----------------------------+
```

The core CLI is a single cross-platform binary (Native AOT, no .NET runtime required). Language wrappers are thin shims that invoke the CLI — no logic lives in the wrappers.

## Grafana Dashboard

Using the `loki` sink? [`dashboards/diagnyx-logs.json`](dashboards/diagnyx-logs.json) is a starter dashboard — log volume by level and by source, plus a raw log browser — that imports directly into Grafana. See [dashboards/README.md](dashboards/README.md) for import steps.

## Prometheus Metrics

An opt-in `/metrics` endpoint exposes log counts by level and source, independent of which sink is active. Disabled by default — enable it and run the server alongside your application:

```json
{ "metrics": { "enabled": true } }
```

```bash
diagnyx metrics serve --port 9464
```

See [docs/CONFIG.md](docs/CONFIG.md#metrics) for details.

## Roadmap

| Phase | Scope |
|-------|-------|
| **v1 (current)** | Structured logger, file sink, RDBMS sinks (SQLite/Postgres/MySQL/MSSQL), .NET and Node.js SDKs |
| **Phase 2** | OpenTelemetry alignment, Grafana/Loki exporter, observability dashboards |
| **Phase 3** | AI-assisted root-cause analysis (`diagnyx ask`) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) — Copyright (c) 2025 Nachiket Ghatole
