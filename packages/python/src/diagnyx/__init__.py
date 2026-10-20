"""Thin Python client for the Diagnyx structured logging CLI."""

from ._logger import DiagnyxLogger, TraceContext

try:
    from importlib.metadata import PackageNotFoundError, version

    __version__ = version("diagnyx")
except PackageNotFoundError:  # running from a source checkout without installing
    __version__ = "0.0.0"

__all__ = ["DiagnyxLogger", "TraceContext", "__version__"]
