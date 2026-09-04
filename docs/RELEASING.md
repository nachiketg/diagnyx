# Releasing Diagnyx

All three packages (CLI binary, `diagnyx-dotnet` NuGet, `diagnyx-node` npm) are versioned together and released from a single git tag. The release workflow builds, publishes, and creates the GitHub Release automatically.

## Prerequisites

Before your first release, add these secrets to the GitHub repository (`Settings > Secrets and variables > Actions`):

| Secret | Where to get it |
|--------|-----------------|
| `NUGET_API_KEY` | [nuget.org API keys](https://www.nuget.org/account/apikeys) — scoped to `diagnyx-dotnet`, Push permission |
| `NPM_TOKEN` | `npm token create` or [npmjs.com Access Tokens](https://www.npmjs.com/settings/<user>/tokens) — Automation or Publish type |

## Release process

1. **Bump the version** across all packages with the helper script:

   ```bash
   ./scripts/bump-version.sh 0.2.0
   ```

   This updates `VERSION`, `core/Diagnyx.Core.csproj`, `packages/dotnet/Diagnyx.Client.csproj`, and `packages/node/package.json`.

2. **Commit and tag:**

   ```bash
   git add -A
   git commit -m "chore: bump version to 0.2.0"
   git tag v0.2.0
   git push && git push origin v0.2.0
   ```

3. **GitHub Actions takes over** — the `release.yml` workflow triggers on the `v*` tag and:
   - Builds Native AOT binaries for `linux-x64`, `win-x64`, `osx-arm64` (each with the version baked in via `-p:Version=<version>`)
   - Creates a GitHub Release with the three platform binaries attached and auto-generated release notes
   - Stages those same three binaries into `packages/dotnet/runtimes/{rid}/native/diagnyx.bin` so the NuGet package bundles them (DX-017)
   - Packs and pushes `diagnyx-dotnet` to NuGet.org (`NUGET_API_KEY`)
   - Publishes `diagnyx-node` to npmjs.com (`NPM_TOKEN`)

## Version format

All packages use the same `MAJOR.MINOR.PATCH` version string (no `v` prefix in the packages themselves; the git tag uses the `v` prefix).

The git tag is the single source of truth for a release. The `VERSION` file and package manifests track the current development version and must be kept in sync using the bump script.

## Verifying a release

After the workflow completes:

```bash
# CLI binary
diagnyx --version   # prints: diagnyx 0.2.0

# NuGet
dotnet add package diagnyx-dotnet --version 0.2.0

# npm
npm install diagnyx-node@0.2.0
```
