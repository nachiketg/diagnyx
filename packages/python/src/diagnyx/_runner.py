"""Locates the diagnyx binary and invokes it in a subprocess."""

import os
import shutil
import stat
import subprocess
import sys
from pathlib import Path
from typing import List, Optional

_BINARY_NAME = "diagnyx.exe" if sys.platform == "win32" else "diagnyx"

# Matches the .NET wrapper's CreateNoWindow: no console window flashes up
# for each log call when the host is a GUI app on Windows.
_CREATION_FLAGS = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def resolve_binary(explicit: Optional[str]) -> Optional[str]:
    """Returns the first diagnyx binary found, or None.

    Order: explicit path, DIAGNYX_PATH, the binary bundled in this package's
    platform wheel, then diagnyx on PATH.
    """
    return explicit or os.environ.get("DIAGNYX_PATH") or _find_bundled() or shutil.which("diagnyx")


def run(binary: str, args: List[str], traceparent: Optional[str] = None) -> int:
    """Invokes the CLI and returns its exit code.

    Spawn failures are caught and reported as a warning rather than raised,
    so a missing or broken CLI never crashes the host application.

    traceparent, when given, is set as the TRACEPARENT environment variable
    for this invocation only. Diagnyx Core validates and parses it (see
    core/Logging/TraceContext.cs); an invalid value is ignored.
    """
    env = None
    if traceparent:
        env = dict(os.environ)
        env["TRACEPARENT"] = traceparent

    try:
        result = subprocess.run(
            [binary, *args],
            stdin=subprocess.DEVNULL,
            env=env,
            creationflags=_CREATION_FLAGS,
            check=False,
        )
    except Exception as exc:  # noqa: BLE001 - logging must never crash the host
        warn(f"failed to invoke diagnyx CLI: {exc}")
        return 1

    # A negative return code means the CLI was killed by a signal.
    return result.returncode if result.returncode >= 0 else 1


def warn(message: str) -> None:
    sys.stderr.write(f"diagnyx: warning: {message.rstrip('.')}. Log entry dropped.\n")
    sys.stderr.flush()


def _find_bundled() -> Optional[str]:
    """Looks for the binary shipped inside this package's platform wheel.

    The release workflow stages it into diagnyx/bin/ before building the
    win_amd64, manylinux x86_64, and macOS arm64 wheels (see
    scripts/build-dists.sh). The pure wheel and sdist ship without it, so
    on other platforms this falls through to PATH discovery.
    """
    candidate = Path(__file__).resolve().parent / "bin" / _BINARY_NAME
    if not candidate.is_file():
        return None

    if sys.platform != "win32" and not os.access(candidate, os.X_OK):
        # Installers normally keep the wheel's executable bit, but not all
        # of them do; restoring it is best effort.
        try:
            mode = candidate.stat().st_mode
            candidate.chmod(mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
        except OSError:
            pass

    return str(candidate)
