#!/usr/bin/env bash
# Usage: ./scripts/bump-version.sh <new-version>
# Updates all package version strings in one step. Run from the repo root.
set -euo pipefail

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
  echo "Usage: $0 <new-version>  (e.g. $0 0.2.0)" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Validate semver (major.minor.patch)
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "error: version must be in major.minor.patch format (got '$VERSION')" >&2
  exit 1
fi

echo "Bumping to $VERSION ..."

# 1. VERSION file
echo "$VERSION" > "$ROOT/VERSION"
echo "  VERSION"

# 2. Core CLI csproj
CORE_CSPROJ="$ROOT/core/Diagnyx.Core.csproj"
sed -i.bak "s|<Version>[^<]*</Version>|<Version>$VERSION</Version>|g" "$CORE_CSPROJ"
rm -f "$CORE_CSPROJ.bak"
echo "  core/Diagnyx.Core.csproj"

# 3. .NET SDK csproj
DOTNET_CSPROJ="$ROOT/packages/dotnet/Diagnyx.Client.csproj"
sed -i.bak "s|<Version>[^<]*</Version>|<Version>$VERSION</Version>|g" "$DOTNET_CSPROJ"
rm -f "$DOTNET_CSPROJ.bak"
echo "  packages/dotnet/Diagnyx.Client.csproj"

# 4. Node.js package.json
NODE_PKG="$ROOT/packages/node/package.json"
sed -i.bak "s|\"version\": \"[^\"]*\"|\"version\": \"$VERSION\"|g" "$NODE_PKG"
rm -f "$NODE_PKG.bak"
echo "  packages/node/package.json"

echo ""
echo "Done. Review the diff, then commit and tag:"
echo "  git add -A"
echo "  git commit -m \"chore: bump version to $VERSION\""
echo "  git tag v$VERSION"
echo "  git push && git push origin v$VERSION"
