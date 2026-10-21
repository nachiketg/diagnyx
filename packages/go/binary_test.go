package diagnyx

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync/atomic"
	"testing"
)

// isolateDiscovery gives the test an empty cache directory and PATH, so only
// what the test sets up can be found.
func isolateDiscovery(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()
	t.Setenv("HOME", dir)           // macOS cache dir, Linux fallback
	t.Setenv("XDG_CACHE_HOME", dir) // Linux
	t.Setenv("LocalAppData", dir)   // Windows
	t.Setenv("PATH", "")
	t.Setenv("DIAGNYX_PATH", "")
	t.Setenv("DIAGNYX_NO_DOWNLOAD", "")
	return dir
}

func writeExecutable(t *testing.T, path string, content []byte) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, content, 0o755); err != nil {
		t.Fatal(err)
	}
}

func binaryName() string {
	if runtime.GOOS == "windows" {
		return "diagnyx.exe"
	}
	return "diagnyx"
}

func TestResolveBinaryOrder(t *testing.T) {
	dir := isolateDiscovery(t)
	t.Setenv("DIAGNYX_NO_DOWNLOAD", "1")

	onPath := filepath.Join(dir, "bin", binaryName())
	writeExecutable(t, onPath, []byte("x"))
	t.Setenv("PATH", filepath.Dir(onPath))
	if got := resolveBinary(""); got != onPath {
		t.Errorf("PATH: got %q, want %q", got, onPath)
	}

	cached, err := cachedBinaryPath()
	if err != nil {
		t.Fatal(err)
	}
	writeExecutable(t, cached, []byte("x"))
	if got := resolveBinary(""); got != cached {
		t.Errorf("cache over PATH: got %q, want %q", got, cached)
	}

	t.Setenv("DIAGNYX_PATH", "/from/env")
	if got := resolveBinary(""); got != "/from/env" {
		t.Errorf("DIAGNYX_PATH over cache: got %q", got)
	}

	if got := resolveBinary("/explicit"); got != "/explicit" {
		t.Errorf("explicit over DIAGNYX_PATH: got %q", got)
	}
}

// releaseServer serves content as this platform's release asset for Version,
// and counts requests.
func releaseServer(t *testing.T, content []byte) *atomic.Int32 {
	t.Helper()
	asset, ok := releaseAssets[runtime.GOOS+"/"+runtime.GOARCH]
	if !ok {
		t.Skipf("no release asset for %s/%s", runtime.GOOS, runtime.GOARCH)
	}
	var requests atomic.Int32
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		requests.Add(1)
		if r.URL.Path != "/v"+Version+"/"+asset {
			http.NotFound(w, r)
			return
		}
		w.Write(content)
	}))
	t.Cleanup(srv.Close)

	prev := releaseBaseURL
	releaseBaseURL = srv.URL
	t.Cleanup(func() { releaseBaseURL = prev })
	return &requests
}

func TestDownload(t *testing.T) {
	isolateDiscovery(t)
	requests := releaseServer(t, []byte("binary"))

	path, err := Download(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if want, _ := cachedBinaryPath(); path != want {
		t.Errorf("path = %q, want %q", path, want)
	}
	if data, _ := os.ReadFile(path); !bytes.Equal(data, []byte("binary")) {
		t.Errorf("content = %q", data)
	}
	if info, _ := os.Stat(path); runtime.GOOS != "windows" && info.Mode()&0o111 == 0 {
		t.Errorf("mode = %v, want executable", info.Mode())
	}

	if _, err := Download(context.Background()); err != nil || requests.Load() != 1 {
		t.Errorf("second Download: err=%v, requests=%d, want a cache hit", err, requests.Load())
	}
}

func TestDownloadFailureLeavesNothingBehind(t *testing.T) {
	isolateDiscovery(t)
	releaseServer(t, nil)
	releaseBaseURL += "/missing"

	if _, err := Download(context.Background()); err == nil || !strings.Contains(err.Error(), "404") {
		t.Errorf("err = %v, want a 404", err)
	}
	cached, _ := cachedBinaryPath()
	if entries, _ := os.ReadDir(filepath.Dir(cached)); len(entries) != 0 {
		t.Errorf("left behind %v", entries)
	}
}

// TestFirstLogDownloadsBinary serves this test binary as the release asset,
// so the downloaded "CLI" is the fake one and the whole flow runs for real.
func TestFirstLogDownloadsBinary(t *testing.T) {
	isolateDiscovery(t)
	self, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	content, err := os.ReadFile(self)
	if err != nil {
		t.Fatal(err)
	}
	requests := releaseServer(t, content)
	_, call := fakeCLI(t, 0)
	l := newTestLogger(t)

	for i := 0; i < 2; i++ {
		if err := l.Info("m"); err != nil {
			t.Fatal(err)
		}
	}
	if got := call().Args[4]; got != "m" {
		t.Errorf("message = %q", got)
	}
	if requests.Load() != 1 {
		t.Errorf("requests = %d, want 1", requests.Load())
	}
}

func TestNoDownloadWhenDisabled(t *testing.T) {
	isolateDiscovery(t)
	requests := releaseServer(t, []byte("binary"))
	t.Setenv("DIAGNYX_NO_DOWNLOAD", "1")
	captureWarnings(t)

	if got := resolveBinary(""); got != "" || requests.Load() != 0 {
		t.Errorf("got %q after %d requests, want no download", got, requests.Load())
	}
}

func TestDownloadFailureWarns(t *testing.T) {
	isolateDiscovery(t)
	releaseServer(t, nil)
	releaseBaseURL += "/missing"
	warnings := captureWarnings(t)

	if got := resolveBinary(""); got != "" {
		t.Errorf("got %q, want none", got)
	}
	if !strings.Contains(warnings.String(), "could not download the diagnyx binary") {
		t.Errorf("warning = %q", warnings)
	}
}
