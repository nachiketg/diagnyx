// Package diagnyx is a thin Go client for the Diagnyx structured logging CLI.
//
// A Logger writes each entry by running "diagnyx log" in a subprocess, so
// entries reach whichever sinks diagnyx.config.json configures. Logging never
// panics: if the binary is missing or fails to start, the call prints a
// warning to stderr and returns an error, which callers are free to ignore.
package diagnyx

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"strings"
	"sync"
)

// Fields is structured metadata written to a log entry's "context" field.
type Fields map[string]any

// ErrBinaryNotFound is returned by log calls when no diagnyx binary could be
// found or downloaded.
var ErrBinaryNotFound = errors.New("diagnyx: binary not found")

// stderr is where warnings go; a variable so tests can capture them.
var stderr io.Writer = os.Stderr

// Logger writes structured log entries through the diagnyx CLI. It is safe
// for concurrent use.
type Logger struct {
	source     string
	binaryPath string

	resolveOnce sync.Once
	binary      string
}

// Option configures a Logger.
type Option func(*Logger)

// WithBinaryPath sets an explicit path to the diagnyx binary, skipping
// discovery.
func WithBinaryPath(path string) Option {
	return func(l *Logger) { l.binaryPath = path }
}

// NewLogger returns a Logger that writes source into every entry's "source"
// field. The binary is located on the first log call, not here.
func NewLogger(source string, opts ...Option) (*Logger, error) {
	if strings.TrimSpace(source) == "" {
		return nil, errors.New("diagnyx: source must not be empty")
	}
	l := &Logger{source: source}
	for _, opt := range opts {
		opt(l)
	}
	return l, nil
}

// Debug writes a debug-level entry.
func (l *Logger) Debug(msg string, fields ...Fields) error {
	return l.Log(context.Background(), "debug", msg, fields...)
}

// Info writes an info-level entry.
func (l *Logger) Info(msg string, fields ...Fields) error {
	return l.Log(context.Background(), "info", msg, fields...)
}

// Warn writes a warn-level entry.
func (l *Logger) Warn(msg string, fields ...Fields) error {
	return l.Log(context.Background(), "warn", msg, fields...)
}

// Error writes an error-level entry.
func (l *Logger) Error(msg string, fields ...Fields) error {
	return l.Log(context.Background(), "error", msg, fields...)
}

// Fatal writes a fatal-level entry. Unlike log.Fatal, it does not exit.
func (l *Logger) Fatal(msg string, fields ...Fields) error {
	return l.Log(context.Background(), "fatal", msg, fields...)
}

// DebugContext writes a debug-level entry with the trace context from ctx.
func (l *Logger) DebugContext(ctx context.Context, msg string, fields ...Fields) error {
	return l.Log(ctx, "debug", msg, fields...)
}

// InfoContext writes an info-level entry with the trace context from ctx.
func (l *Logger) InfoContext(ctx context.Context, msg string, fields ...Fields) error {
	return l.Log(ctx, "info", msg, fields...)
}

// WarnContext writes a warn-level entry with the trace context from ctx.
func (l *Logger) WarnContext(ctx context.Context, msg string, fields ...Fields) error {
	return l.Log(ctx, "warn", msg, fields...)
}

// ErrorContext writes an error-level entry with the trace context from ctx.
func (l *Logger) ErrorContext(ctx context.Context, msg string, fields ...Fields) error {
	return l.Log(ctx, "error", msg, fields...)
}

// FatalContext writes a fatal-level entry with the trace context from ctx.
// Unlike log.Fatal, it does not exit.
func (l *Logger) FatalContext(ctx context.Context, msg string, fields ...Fields) error {
	return l.Log(ctx, "fatal", msg, fields...)
}

// Log writes an entry at level (debug, info, warn, error, or fatal). Multiple
// fields are merged, with later keys winning. ctx supplies the trace context
// set by WithTraceContext; its cancellation does not stop the write.
func (l *Logger) Log(ctx context.Context, level, msg string, fields ...Fields) error {
	l.resolveOnce.Do(func() { l.binary = resolveBinary(l.binaryPath) })
	if l.binary == "" {
		warn("diagnyx binary not found. Install it from https://github.com/nachiketg/diagnyx/releases " +
			"or set the DIAGNYX_PATH environment variable")
		return ErrBinaryNotFound
	}

	args := []string{"log", "--level", level, "--message", msg, "--source", l.source}
	if merged := merge(fields); merged != nil {
		data, err := json.Marshal(merged)
		if err != nil {
			warn("fields are not JSON-serializable: " + err.Error())
			return fmt.Errorf("diagnyx: %w", err)
		}
		args = append(args, "--context", string(data))
	}

	cmd := exec.Command(l.binary, args...)
	cmd.Stdout, cmd.Stderr = os.Stdout, os.Stderr
	if tp := traceparent(ctx); tp != "" {
		cmd.Env = append(os.Environ(), "TRACEPARENT="+tp)
	}

	err := cmd.Run()
	var exitErr *exec.ExitError
	if errors.As(err, &exitErr) {
		// The CLI has already printed why to stderr.
		return fmt.Errorf("diagnyx: log exited with code %d", exitErr.ExitCode())
	}
	if err != nil {
		warn("failed to invoke diagnyx CLI: " + err.Error())
		return fmt.Errorf("diagnyx: %w", err)
	}
	return nil
}

type traceKey struct{}

type traceContext struct{ traceID, spanID string }

// WithTraceContext returns a copy of ctx carrying a W3C trace ID (32 lowercase
// hex chars) and span ID (16). Every ...Context log call made with it, or a
// context derived from it, is correlated with that trace.
//
// The Diagnyx core validates the IDs; invalid ones leave traceId and spanId
// null on the entry rather than failing the write.
func WithTraceContext(ctx context.Context, traceID, spanID string) context.Context {
	return context.WithValue(ctx, traceKey{}, traceContext{traceID, spanID})
}

func traceparent(ctx context.Context) string {
	if ctx == nil {
		return ""
	}
	tc, ok := ctx.Value(traceKey{}).(traceContext)
	if !ok {
		return ""
	}
	return "00-" + tc.traceID + "-" + tc.spanID + "-01"
}

func merge(fields []Fields) Fields {
	merged := Fields{}
	for _, f := range fields {
		for k, v := range f {
			merged[k] = v
		}
	}
	if len(merged) == 0 {
		return nil
	}
	return merged
}

func warn(msg string) {
	fmt.Fprintf(stderr, "diagnyx: warning: %s. Log entry dropped.\n", strings.TrimSuffix(msg, "."))
}
