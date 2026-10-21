package diagnyx

import (
	"bufio"
	"context"
	"encoding/json"
	"os"
	"testing"
)

// TestIntegration writes entries through a real diagnyx CLI and reads them
// back from its file sink. CI runs it with DIAGNYX_PATH and DIAGNYX_CONFIG
// set, and DIAGNYX_INTEGRATION_LOG naming the sink's file; otherwise it is
// skipped.
func TestIntegration(t *testing.T) {
	logPath := os.Getenv("DIAGNYX_INTEGRATION_LOG")
	if logPath == "" {
		t.Skip("set DIAGNYX_INTEGRATION_LOG (plus DIAGNYX_PATH and DIAGNYX_CONFIG) to run against a real CLI")
	}

	l, err := NewLogger("sdk-test-go")
	if err != nil {
		t.Fatal(err)
	}
	ctx := WithTraceContext(context.Background(), "4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7")
	for _, err := range []error{
		l.Info("go SDK smoke test", Fields{"ci": true}),
		l.WarnContext(ctx, "go SDK trace context"),
	} {
		if err != nil {
			t.Fatal(err)
		}
	}

	entries := readEntries(t, logPath)
	smoke, trace := entries["go SDK smoke test"], entries["go SDK trace context"]
	if smoke.Level != "info" || smoke.Source != "sdk-test-go" || smoke.Context["ci"] != true || smoke.TraceID != nil {
		t.Errorf("smoke entry = %+v", smoke)
	}
	if trace.Level != "warn" || deref(trace.TraceID) != "4bf92f3577b34da6a3ce929d0e0e4736" ||
		deref(trace.SpanID) != "00f067aa0ba902b7" {
		t.Errorf("trace entry = %+v", trace)
	}
}

type entry struct {
	Level   string
	Source  string
	Context map[string]any
	TraceID *string
	SpanID  *string
}

// readEntries parses a JSON Lines log file, keyed by message.
func readEntries(t *testing.T, path string) map[string]entry {
	t.Helper()
	f, err := os.Open(path)
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()

	entries := map[string]entry{}
	scanner := bufio.NewScanner(f)
	for scanner.Scan() {
		var e struct {
			entry
			Message string
		}
		if err := json.Unmarshal(scanner.Bytes(), &e); err != nil {
			t.Fatalf("invalid JSON line %q: %v", scanner.Text(), err)
		}
		entries[e.Message] = e.entry
	}
	if err := scanner.Err(); err != nil {
		t.Fatal(err)
	}
	return entries
}

func deref(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}
