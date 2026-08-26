// Smoke test for the diagnyx-dotnet SDK.
// Requires DIAGNYX_PATH to point to a built diagnyx binary.
// Exit code mirrors the logger result: 0 = pass, 1 = fail.
using Diagnyx;

var logger = new DiagnyxLogger("sdk-test");

int code = logger.Info("dotnet SDK smoke test", new { ci = true });
return code;
