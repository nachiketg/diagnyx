# Smoke test for wrapper-supplied trace context: an explicit trace_context
# argument, ambient context via DiagnyxLogger.trace_context() (contextvars),
# including across an await and into a child asyncio task, and the active
# OpenTelemetry span when opentelemetry-api is installed.

import asyncio
import sys

from diagnyx import DiagnyxLogger, TraceContext

logger = DiagnyxLogger("trace-context-smoke-test-python")


async def main() -> int:
    code = 0

    code |= logger.info(
        "explicit trace context",
        None,
        TraceContext("4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7"),
    )

    with DiagnyxLogger.trace_context("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb"):
        await asyncio.sleep(0.005)
        code |= logger.warn("ambient trace context after await")

        async def child() -> int:
            return logger.info("ambient trace context in child task")

        code |= await asyncio.create_task(child())

    code |= logger.info("no trace context outside the scope")

    try:
        from opentelemetry import trace
        from opentelemetry.trace import NonRecordingSpan, SpanContext, TraceFlags
    except ImportError:
        print("opentelemetry-api not installed; skipping the OpenTelemetry span check")
    else:
        span = NonRecordingSpan(
            SpanContext(
                trace_id=0xCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC,
                span_id=0xDDDDDDDDDDDDDDDD,
                is_remote=False,
                trace_flags=TraceFlags(TraceFlags.SAMPLED),
            )
        )
        with trace.use_span(span):
            code |= logger.info("opentelemetry span trace context")

    return code


sys.exit(asyncio.run(main()))
