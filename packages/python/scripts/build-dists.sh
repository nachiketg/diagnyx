#!/usr/bin/env bash
# Usage: packages/python/scripts/build-dists.sh <binaries-dir> <out-dir>
#
# Builds every distribution of the diagnyx PyPI package into <out-dir>:
#   - an sdist and a pure py3-none-any wheel, with no binary (any platform;
#     DiagnyxLogger falls back to DIAGNYX_PATH or PATH)
#   - one wheel per supported platform, each bundling that platform's
#     diagnyx binary under diagnyx/bin/
#
# <binaries-dir> must hold the release asset names: diagnyx-win-x64.exe,
# diagnyx-linux-x64, diagnyx-osx-arm64. Requires `pip install build`.
set -euo pipefail

BIN_SRC="${1:?usage: $0 <binaries-dir> <out-dir>}"
OUT="${2:?usage: $0 <binaries-dir> <out-dir>}"
BIN_SRC="$(cd "$BIN_SRC" && pwd)"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

PKG="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="$PKG/src/diagnyx/bin"
trap 'rm -rf "$STAGE"' EXIT
rm -rf "$STAGE"

# The sdist and pure wheel are built first, before any binary is staged.
python -m build --sdist --wheel --outdir "$OUT" "$PKG"

# asset name | name inside the wheel | wheel platform tag
# manylinux_2_34: the AOT binary needs glibc 2.34 (built on ubuntu-latest).
while IFS='|' read -r asset binary platform; do
  test -f "$BIN_SRC/$asset" || { echo "error: missing $BIN_SRC/$asset" >&2; exit 1; }
  rm -rf "$STAGE"
  mkdir -p "$STAGE"
  cp "$BIN_SRC/$asset" "$STAGE/$binary"
  chmod 755 "$STAGE/$binary"
  DIAGNYX_WHEEL_PLATFORM="$platform" python -m build --wheel --outdir "$OUT" "$PKG"
done <<'EOF'
diagnyx-win-x64.exe|diagnyx.exe|win_amd64
diagnyx-linux-x64|diagnyx|manylinux_2_34_x86_64
diagnyx-osx-arm64|diagnyx|macosx_11_0_arm64
EOF

ls -1 "$OUT"
