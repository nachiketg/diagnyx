"""DiagnyxLogger: thin Python client for the Diagnyx structured logging CLI."""

import contextlib
import contextvars
import json
import os
from typing import Any, Iterator, NamedTuple, Optional, Union

from . import _runner

try:
    from opentelemetry import trace as _otel_trace
except ImportError:  # OpenTelemetry is optional; without it there is no span to pick up.
    _otel_trace = None


class TraceContext(NamedTuple):
    """W3C trace and span IDs (32 and 16 lowercase hex chars) for one log entry."""

    trace_id: str
    span_id: str


# Ambient trace context: set via DiagnyxLogger.trace_context() and picked up
# automatically by every log call made within that scope (e.g. for the
# lifetime of one request), without threading it through call sites.
# contextvars carries it across awaits and into asyncio tasks created inside
# the scope, like AsyncLocalStorage in the Node SDK.
_ambient_trace_context: "contextvars.ContextVar[Optional[TraceContext]]" = contextvars.ContextVar(
    "diagnyx_trace_context", default=None
)


class DiagnyxLogger:
    """Writes structured log entries by spawning ``diagnyx log``.

    Every method returns the CLI exit code (0 = success, 1 = failure). If the
    binary cannot be found or fails to start, log calls do not raise: they
    print a warning to stderr and return 1.
    """

    def __init__(self, source: str, binary_path: Optional[Union[str, "os.PathLike[str]"]] = None) -> None:
        if not isinstance(source, str) or not source.strip():
            raise ValueError("source must be a non-empty string")
        self._source = source
        self._binary = _runner.resolve_binary(os.fspath(binary_path) if binary_path is not None else None)

    def debug(self, message: str, context: Any = None, trace_context: Optional[TraceContext] = None) -> int:
        return self.log("debug", message, context, trace_context)

    def info(self, message: str, context: Any = None, trace_context: Optional[TraceContext] = None) -> int:
        return self.log("info", message, context, trace_context)

    def warn(self, message: str, context: Any = None, trace_context: Optional[TraceContext] = None) -> int:
        return self.log("warn", message, context, trace_context)

    # Alias matching the standard library's logging.Logger.warning.
    warning = warn

    def error(self, message: str, context: Any = None, trace_context: Optional[TraceContext] = None) -> int:
        return self.log("error", message, context, trace_context)

    def fatal(self, message: str, context: Any = None, trace_context: Optional[TraceContext] = None) -> int:
        return self.log("fatal", message, context, trace_context)

    def log(self, level: str, message: str, context: Any = None, trace_context: Optional[TraceContext] = None) -> int:
        """Writes a log entry at ``level`` (debug, info, warn, error, fatal).

        ``context`` accepts any JSON-serializable object or a pre-serialized
        JSON object string. ``trace_context`` is an explicit
        ``(trace_id, span_id)`` pair; when omitted, the ambient one set by
        :meth:`trace_context` is used, then the active OpenTelemetry span.
        """
        if not self._binary:
            _runner.warn(
                "diagnyx binary not found. Install it from https://github.com/nachiketg/diagnyx/releases "
                "or set the DIAGNYX_PATH environment variable"
            )
            return 1

        args = ["log", "--level", level, "--message", str(message), "--source", self._source]

        if context is not None:
            if isinstance(context, str):
                context_json = context
            else:
                try:
                    context_json = json.dumps(context, default=_json_default, separators=(",", ":"))
                except (TypeError, ValueError) as exc:
                    _runner.warn(f"context is not JSON-serializable: {exc}")
                    return 1
            args += ["--context", context_json]

        return _runner.run(self._binary, args, _resolve_traceparent(trace_context))

    @staticmethod
    @contextlib.contextmanager
    def trace_context(trace_id: str, span_id: str) -> Iterator[TraceContext]:
        """Sets the ambient trace context for every log call made inside the ``with`` block."""
        token = _ambient_trace_context.set(TraceContext(trace_id, span_id))
        try:
            yield _ambient_trace_context.get()
        finally:
            _ambient_trace_context.reset(token)


# Builds a W3C traceparent header. Diagnyx Core validates the format on read
# (see core/Logging/TraceContext.cs), so this only needs to shape the string:
# malformed input just yields a null traceId/spanId on the log entry.
def _resolve_traceparent(explicit: Optional[TraceContext]) -> Optional[str]:
    for candidate in (explicit, _ambient_trace_context.get()):
        if candidate is None:
            continue
        try:
            trace_id, span_id = candidate
        except (TypeError, ValueError):
            return None
        if not isinstance(trace_id, str) or not isinstance(span_id, str):
            return None
        return f"00-{trace_id}-{span_id}-01"

    return _otel_traceparent()


def _otel_traceparent() -> Optional[str]:
    if _otel_trace is None:
        return None
    span_context = _otel_trace.get_current_span().get_span_context()
    if not span_context.is_valid:
        return None
    flags = int(span_context.trace_flags) & 0xFF
    return f"00-{span_context.trace_id:032x}-{span_context.span_id:016x}-{flags:02x}"


# Lets common non-JSON types (datetime, date, UUID, Decimal, Path) appear in
# context without the caller pre-converting them.
def _json_default(value: Any) -> Any:
    isoformat = getattr(value, "isoformat", None)
    return isoformat() if callable(isoformat) else str(value)
