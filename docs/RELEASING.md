# Releasing Diagnyx

All four packages (CLI binary, `diagnyx-dotnet` NuGet, `diagnyx-node` npm, `diagnyx` PyPI) are versioned together and released from a single git tag. The release workflow builds, publishes, and creates the GitHub Release automatically.

## Prerequisites

Before your first release, add these secrets to the GitHub repository (`Settings > Secrets and variables > Actions`):

| Secret | Where to get it |
|--------|-----------------|
| `NUGET_API_KEY` | [nuget.org API keys](https://www.nuget.org/account/apikeys) — scoped to `diagnyx-dotnet`, Push permission |
| `NPM_TOKEN` | `npm token create` or [npmjs.com Access Tokens](https://www.npmjs.com/settings/<user>/tokens) — Automation or Publish type |
| `PYPI_API_TOKEN` | [pypi.org API tokens](https://pypi.org/manage/account/token/) — the first upload needs an account-scoped token (the `diagnyx` project doesn't exist on PyPI until then); afterwards, replace it with one scoped to `diagnyx` |

## Release process

1. **Bump the version** across all packages with the helper script:

   ```bash
   ./scripts/bump-version.sh 0.2.0
   ```

   This updates `VERSION`, `core/Diagnyx.Core.csproj`, `packages/dotnet/Diagnyx.Client.csproj`, `packages/node/package.json`, and `packages/python/pyproject.toml`.

2. **Commit and tag:**

   ```bash
   git add -A
   git commit -m "chore: bump version to 0.2.0"
   git tag v0.2.0
   git push && git push origin v0.2.0
   ```

3. **GitHub Actions takes over** — the `release.yml` workflow triggers on the `v*` tag and:
   - Builds Native AOT binaries for `linux-x64`, `win-x64`, `osx-arm64` (each with the version baked in via `-p:Version=<version>`)
   - Verifies each binary works on its own, copied into an empty folder, by writing to the SQLite sink with metrics enabled (DX-060). The SQLite native library is embedded in the binary and extracted at runtime (see [CONFIG.md](CONFIG.md#the-sqlite-native-library-diagnyxnative)); this check stops a release if that ever breaks again, before anything is published
   - Creates a GitHub Release with the three platform binaries attached and auto-generated release notes, named `diagnyx-win-x64.exe`, `diagnyx-linux-x64`, `diagnyx-osx-arm64`
   - Stages those same three binaries into `packages/dotnet/runtimes/{rid}/native/diagnyx.bin` so the NuGet package bundles them (DX-017)
   - Packs and pushes `diagnyx-dotnet` to NuGet.org (`NUGET_API_KEY`)
   - Publishes `diagnyx-node` to npmjs.com (`NPM_TOKEN`)
   - Builds and uploads `diagnyx` to PyPI (`PYPI_API_TOKEN`) via `packages/python/scripts/build-dists.sh` (DX-057): an sdist and a pure `py3-none-any` wheel with no binary, plus one wheel per platform bundling that platform's release binary as `diagnyx/bin/diagnyx[.exe]` — tagged `win_amd64`, `manylinux_2_34_x86_64`, and `macosx_11_0_arm64`. pip picks the most specific wheel for the installing machine, so supported platforms get the bundled binary and everything else falls back to `DIAGNYX_PATH`/PATH

   The GitHub Release is created before the npm publish step runs, and must stay that way: `diagnyx-node`'s `postinstall` script (DX-020) downloads `diagnyx-{rid}[.exe]` from the GitHub Release tagged `v<version>` matching the installed package version, so a fresh `npm install` right after publish needs that release asset to already exist. If you ever rename the release asset files, update `packages/node/scripts/postinstall.js`'s `assetName()` and the asset table in `packages/python/scripts/build-dists.sh` to match.

   The Linux wheel's `manylinux_2_34` tag reflects the glibc symbol versions the Native AOT binary links against when built on `ubuntu-latest` (check with `strings diagnyx-linux-x64 | grep -oE 'GLIBC_[0-9.]+' | sort -uV | tail -1`). If the build runner image changes, re-check this and update the tag in `build-dists.sh`. pip trusts the tag at install time, so a tag claiming older glibc support than the binary really needs is not caught until runtime, where the binary fails to start and `DiagnyxLogger` drops entries with a warning.

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

# PyPI
pip install diagnyx==0.2.0
```
