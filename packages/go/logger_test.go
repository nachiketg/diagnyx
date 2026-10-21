package diagnyx

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"strconv"
	"strings"
	"testing"
)

// TestMain lets this test binary stand in for the diagnyx CLI: when
// DIAGNYX_FAKE_CLI_OUT is set, it records how it was invoked and exits.
func TestMain(m *testing.M) {
	if out := os.Getenv("DIAGNYX_FAKE_CLI_OUT"); out != "" {
		call := fakeCall{Args: os.Args[1:], Traceparent: os.Getenv("TRACEPARENT")}
		data, _ := json.Marshal(call)
		_ = os.WriteFile(out, data, 0o644)
		code, _ := strconv.Atoi(os.Getenv("DIAGNYX_FAKE_CLI_EXIT"))
		os.Exit(code)
	}
	os.Exit(m.Run())
}

type fakeCall struct {
	Args        []string
	Traceparent string
}

// fakeCLI makes this test binary act as the CLI with the given exit code,
// and returns its path plus a function reading back the recorded call.
func fakeCLI(t *testing.T, exitCode int) (string, func() fakeCall) {
	t.Helper()
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	out := filepath.Join(t.TempDir(), "call.json")
	t.Setenv("DIAGNYX_FAKE_CLI_OUT", out)
	t.Setenv("DIAGNYX_FAKE_CLI_EXIT", strconv.Itoa(exitCode))
	return exe, func() fakeCall {
		t.Helper()
		data, err := os.ReadFile(out)
		if err != nil {
			t.Fatalf("CLI was not invoked: %v", err)
		}
		var call fakeCall
		if err := json.Unmarshal(data, &call); err != nil {
			t.Fatal(err)
		}
		return call
	}
}

// captureWarnings redirects warnings into a buffer for the rest of the test.
func captureWarnings(t *testing.T) *bytes.Buffer {
	t.Helper()
	var buf bytes.Buffer
	prev := stderr
	stderr = &buf
	t.Cleanup(func() { stderr = prev })
	return &buf
}

func newTestLogger(t *testing.T, opts ...Option) *Logger {
	t.Helper()
	l, err := NewLogger("test-service", opts...)
	if err != nil {
		t.Fatal(err)
	}
	return l
}

func TestLogPassesEntryToCLI(t *testing.T) {
	exe, call := fakeCLI(t, 0)
	l := newTestLogger(t, WithBinaryPath(exe))

	if err := l.Info("payment failed", Fields{"orderId": "ord-1", "amount": 49.99}, Fields{"orderId": "ord-2"}); err != nil {
		t.Fatal(err)
	}

	want := []string{"log", "--level", "info", "--message", "payment failed", "--source", "test-service",
		"--context", `{"amount":49.99,"orderId":"ord-2"}`}
	if got := call(); !reflect.DeepEqual(got.Args, want) || got.Traceparent != "" {
		t.Errorf("got %+v, want args %q and no traceparent", got, want)
	}
}

func TestLevelMethods(t *testing.T) {
	exe, call := fakeCLI(t, 0)
	l := newTestLogger(t, WithBinaryPath(exe))
	ctx := context.Background()

	tests := []struct {
		level string
		log   func() error
	}{
		{"debug", func() error { return l.Debug("m") }},
		{"info", func() error { return l.Info("m") }},
		{"warn", func() error { return l.Warn("m") }},
		{"error", func() error { return l.Error("m") }},
		{"fatal", func() error { return l.Fatal("m") }},
		{"debug", func() error { return l.DebugContext(ctx, "m") }},
		{"info", func() error { return l.InfoContext(ctx, "m") }},
		{"warn", func() error { return l.WarnContext(ctx, "m") }},
		{"error", func() error { return l.ErrorContext(ctx, "m") }},
		{"fatal", func() error { return l.FatalContext(ctx, "m") }},
	}
	for _, tt := range tests {
		if err := tt.log(); err != nil {
			t.Fatal(err)
		}
		if got := call().Args[2]; got != tt.level {
			t.Errorf("level = %q, want %q", got, tt.level)
		}
	}
}

func TestNoFieldsOmitsContextFlag(t *testing.T) {
	exe, call := fakeCLI(t, 0)
	l := newTestLogger(t, WithBinaryPath(exe))

	for _, fields := range [][]Fields{nil, {{}}, {nil}} {
		if err := l.Info("m", fields...); err != nil {
			t.Fatal(err)
		}
		if args := call().Args; len(args) != 7 {
			t.Errorf("fields %v: got args %q, want no --context", fields, args)
		}
	}
}

func TestTraceContext(t *testing.T) {
	exe, call := fakeCLI(t, 0)
	l := newTestLogger(t, WithBinaryPath(exe))

	ctx := WithTraceContext(context.Background(), "4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7")
	ctx, cancel := context.WithCancel(ctx) // derived contexts keep it
	cancel()                               // and cancellation doesn't stop the write

	if err := l.InfoContext(ctx, "m"); err != nil {
		t.Fatal(err)
	}
	if got, want := call().Traceparent, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"; got != want {
		t.Errorf("traceparent = %q, want %q", got, want)
	}

	// A nil context must not panic.
	if err := l.InfoContext(nil, "m"); err != nil {
		t.Fatal(err)
	}
	if got := call().Traceparent; got != "" {
		t.Errorf("traceparent = %q, want none", got)
	}
}

func TestNonZeroExitReturnsError(t *testing.T) {
	exe, _ := fakeCLI(t, 3)
	warnings := captureWarnings(t)

	err := newTestLogger(t, WithBinaryPath(exe)).Info("m")
	if err == nil || !strings.Contains(err.Error(), "exited with code 3") {
		t.Errorf("err = %v, want exit code 3", err)
	}
	if warnings.Len() != 0 {
		t.Errorf("unexpected warning %q: the CLI reports its own errors", warnings)
	}
}

func TestMissingBinaryWarnsAndReturnsErrBinaryNotFound(t *testing.T) {
	isolateDiscovery(t)
	t.Setenv("DIAGNYX_NO_DOWNLOAD", "1")
	warnings := captureWarnings(t)

	err := newTestLogger(t).Info("m")
	if !errors.Is(err, ErrBinaryNotFound) {
		t.Errorf("err = %v, want ErrBinaryNotFound", err)
	}
	if !strings.Contains(warnings.String(), "diagnyx: warning: diagnyx binary not found") {
		t.Errorf("warning = %q", warnings)
	}
}

func TestStartFailureWarns(t *testing.T) {
	warnings := captureWarnings(t)

	err := newTestLogger(t, WithBinaryPath(filepath.Join(t.TempDir(), "missing"))).Info("m")
	if err == nil {
		t.Fatal("want an error")
	}
	if !strings.Contains(warnings.String(), "diagnyx: warning: failed to invoke diagnyx CLI") {
		t.Errorf("warning = %q", warnings)
	}
}

func TestUnserializableFieldsWarn(t *testing.T) {
	exe, _ := fakeCLI(t, 0)
	warnings := captureWarnings(t)

	err := newTestLogger(t, WithBinaryPath(exe)).Info("m", Fields{"ch": make(chan int)})
	if err == nil {
		t.Fatal("want an error")
	}
	if !strings.Contains(warnings.String(), "fields are not JSON-serializable") {
		t.Errorf("warning = %q", warnings)
	}
}

func TestNewLoggerRejectsEmptySource(t *testing.T) {
	for _, source := range []string{"", "  "} {
		if _, err := NewLogger(source); err == nil {
			t.Errorf("NewLogger(%q): want an error", source)
		}
	}
}
