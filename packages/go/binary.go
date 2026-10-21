package diagnyx

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"time"
)

// releaseAssets maps GOOS/GOARCH to the binary name on the GitHub Release.
// It must match the asset names in .github/workflows/release.yml.
var releaseAssets = map[string]string{
	"windows/amd64": "diagnyx-win-x64.exe",
	"linux/amd64":   "diagnyx-linux-x64",
	"darwin/arm64":  "diagnyx-osx-arm64",
}

// releaseBaseURL is a variable so tests can point it at a local server.
var releaseBaseURL = "https://github.com/nachiketg/diagnyx/releases/download"

const downloadTimeout = 2 * time.Minute

// resolveBinary returns the first diagnyx binary found, or "" if none:
// explicit path, DIAGNYX_PATH, a previously downloaded copy, PATH, then a
// fresh download (unless DIAGNYX_NO_DOWNLOAD is set).
func resolveBinary(explicit string) string {
	if explicit != "" {
		return explicit
	}
	if path := os.Getenv("DIAGNYX_PATH"); path != "" {
		return path
	}
	if path, err := cachedBinaryPath(); err == nil && fileExists(path) {
		return path
	}
	if path, err := exec.LookPath("diagnyx"); err == nil {
		return path
	}
	if os.Getenv("DIAGNYX_NO_DOWNLOAD") != "" {
		return ""
	}

	ctx, cancel := context.WithTimeout(context.Background(), downloadTimeout)
	defer cancel()
	path, err := Download(ctx)
	if err != nil {
		fmt.Fprintf(stderr, "diagnyx: warning: could not download the diagnyx binary: %v\n", err)
		return ""
	}
	return path
}

// Download fetches the diagnyx binary for this platform (win-x64, linux-x64,
// or osx-arm64) from the GitHub Release matching Version into the user cache
// directory, and returns its path. If it is already there, Download returns
// at once.
//
// Loggers call it automatically when no binary is found. Call it yourself to
// fetch ahead of time, for example at startup or in a Docker build.
func Download(ctx context.Context) (string, error) {
	asset, ok := releaseAssets[runtime.GOOS+"/"+runtime.GOARCH]
	if !ok {
		return "", fmt.Errorf("no prebuilt diagnyx binary for %s/%s; install it from "+
			"https://github.com/nachiketg/diagnyx/releases and set DIAGNYX_PATH", runtime.GOOS, runtime.GOARCH)
	}
	dest, err := cachedBinaryPath()
	if err != nil {
		return "", err
	}
	if fileExists(dest) {
		return dest, nil
	}

	url := fmt.Sprintf("%s/v%s/%s", releaseBaseURL, Version, asset)
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return "", err
	}
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("GET %s: %s", url, resp.Status)
	}

	if err := os.MkdirAll(filepath.Dir(dest), 0o755); err != nil {
		return "", err
	}
	// Download to a temp file and rename it into place, so concurrent
	// processes never run a half-written binary.
	tmp, err := os.CreateTemp(filepath.Dir(dest), filepath.Base(dest)+".*.download")
	if err != nil {
		return "", err
	}
	defer os.Remove(tmp.Name()) // no-op once renamed

	_, err = io.Copy(tmp, resp.Body)
	if closeErr := tmp.Close(); err == nil {
		err = closeErr
	}
	if err == nil {
		err = os.Chmod(tmp.Name(), 0o755)
	}
	if err == nil {
		err = os.Rename(tmp.Name(), dest)
	}
	if err != nil && !fileExists(dest) { // another process may have won the race
		return "", err
	}
	return dest, nil
}

// cachedBinaryPath is where Download puts the binary, e.g.
// ~/.cache/diagnyx/v1.1.0/diagnyx on Linux.
func cachedBinaryPath() (string, error) {
	dir, err := os.UserCacheDir()
	if err != nil {
		return "", err
	}
	name := "diagnyx"
	if runtime.GOOS == "windows" {
		name += ".exe"
	}
	return filepath.Join(dir, "diagnyx", "v"+Version, name), nil
}

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}
