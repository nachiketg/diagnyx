// Smoke test for wrapper-supplied trace context (DX-028): an explicit
// traceContext argument, and ambient Activity.Current propagation.
// Requires DIAGNYX_PATH to point to a built diagnyx binary.
// Exit code is the OR of both log calls: 0 = pass, 1 = fail.
using System.Diagnostics;
using Diagnyx;

var logger = new DiagnyxLogger("trace-context-smoke-test");

int code = logger.Info(
    "explicit trace context",
    traceContext: ("4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7"));

using (var activity = new Activity("trace-context-smoke-test-activity"))
{
    activity.SetIdFormat(ActivityIdFormat.W3C);
    activity.Start();
    code |= logger.Warn("ambient activity trace context");
    activity.Stop();
}

return code;
