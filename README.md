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
│   └── CONFIG.md           # Config file format reference
├── CONTRIBUTING.md
└── LICENSE
```

## Quick Start

> **Note:** v1 is under active development. Installation steps below will be finalized on first release.

### .NET

```bash
dotnet add package Diagnyx
```

```csharp
using Diagnyx;

var logger = new Logger();
logger.Info("App started", new { userId = 42 });
logger.Error("Something went wrong", new { errorCode = 500 });
```

### Node.js

```bash
npm install diagnyx
```

```js
const { createLogger } = require('diagnyx');

const logger = createLogger({ source: 'my-service' });
logger.info('App started', { userId: 42 });
logger.error('Something went wrong', { errorCode: 500 });
```

## Log Format

All log entries are written as [JSON Lines](https://jsonlines.org/) (one JSON object per line). See [docs/SCHEMA.md](docs/SCHEMA.md) for the full field reference.

Example entry:

```json
{"timestamp":"2025-08-25T12:00:00.000Z","level":"info","message":"App started","source":"my-service","context":{"userId":42},"traceId":null,"spanId":null}
```

## Configuration

Diagnyx is configured via a `diagnyx.config.json` file in your project root or `~/.diagnyx/`. Run `diagnyx init` to scaffold a default config. See [docs/CONFIG.md](docs/CONFIG.md) for all options.

## Architecture

```
                 +----------------------+        +-----------------------------+
  .NET app  ---->|                      |        |  File sink (default)        |
                 |  Diagnyx Core (CLI)  |------->|  JSON Lines log file        |
  Node.js app -->|                      |   |    +-----------------------------+
                 +----------------------+   |
        ^                    ^              |    +-----------------------------+
        |                    |              +--->|  RDBMS sink (optional)      |
  diagnyx-dotnet        diagnyx-node             |  SQLite / Postgres /        |
  (NuGet wrapper)      (npm wrapper)             |  MySQL / MSSQL              |
                                                 +-----------------------------+
```

The core CLI is a single cross-platform binary (Native AOT, no .NET runtime required). Language wrappers are thin shims that invoke the CLI — no logic lives in the wrappers.

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
