# Contributing to Diagnyx

Thank you for your interest in contributing! Diagnyx is an open-source project and welcomes contributions of all kinds.

## Getting Started

1. Fork the repository and clone your fork.
2. Create a feature branch from `main`: `git checkout -b feat/your-feature`.
3. Make your changes (see structure below).
4. Open a pull request against `main` with a clear description.

## Repository Layout

| Path | What lives here |
|------|----------------|
| `core/` | Diagnyx Core CLI — the single cross-platform binary built in .NET with Native AOT |
| `packages/dotnet/` | `diagnyx-dotnet` NuGet wrapper |
| `packages/node/` | `diagnyx-node` npm wrapper |
| `docs/` | Schema, config, and design documentation |

## Design Principles

- **Thin wrappers**: language packages must not duplicate logic from the core. They only invoke the CLI binary and handle errors gracefully.
- **Stable contract**: the log entry schema in `docs/SCHEMA.md` is the shared contract. Changes must be additive after v1.
- **Sink abstraction**: new database engines plug into an internal sink interface; the CLI and wrappers are unaffected.
- **Simplicity first**: favor easy-to-build-and-explain over scalable or clever.

## Commit Style

Use short imperative subject lines, e.g.:

```
feat(core): add --context flag to log command
fix(node): handle missing binary path gracefully
docs: clarify RDBMS sink config options
```

Prefixes: `feat`, `fix`, `docs`, `test`, `chore`, `refactor`.

## Code Standards

- **Core (.NET)**: follow standard C# conventions; use `dotnet format` before committing.
- **Node.js wrapper**: ESLint + Prettier; run `npm run lint` before committing.
- All public APIs must match the shape documented in `docs/SCHEMA.md` and the requirements doc.

## Reporting Issues

Open a GitHub Issue with:
- What you expected to happen.
- What actually happened (paste relevant log output or error messages).
- Your OS, .NET or Node.js version, and Diagnyx version.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
