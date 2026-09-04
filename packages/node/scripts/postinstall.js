'use strict';

// Downloads the diagnyx binary matching this package's own version and the
// current OS/architecture from GitHub Releases, into bin/ alongside this
// package. Never fails `npm install`: any error here (unsupported platform,
// network failure, no matching release yet) is a warning, not a fatal exit,
// since DiagnyxLogger still falls back to DIAGNYX_PATH/PATH at call time.

const https = require('https');
const fs = require('fs');
const path = require('path');

const MAX_REDIRECTS = 5;
const DOWNLOAD_TIMEOUT_MS = 30000;

function ridFor(platform, arch) {
  if (platform === 'win32' && arch === 'x64') return 'win-x64';
  if (platform === 'linux' && arch === 'x64') return 'linux-x64';
  if (platform === 'darwin' && arch === 'arm64') return 'osx-arm64';
  return null;
}

function assetName(rid) {
  return rid === 'win-x64' ? 'diagnyx-win-x64.exe' : `diagnyx-${rid}`;
}

function binaryName(platform) {
  return platform === 'win32' ? 'diagnyx.exe' : 'diagnyx';
}

function download(url, redirectsLeft, onResponse, onError) {
  const req = https.get(url, { headers: { 'User-Agent': 'diagnyx-node-postinstall' } }, (res) => {
    const { statusCode, headers } = res;

    if (statusCode >= 300 && statusCode < 400 && headers.location) {
      res.resume();
      if (redirectsLeft <= 0) {
        onError(new Error('too many redirects'));
        return;
      }
      download(headers.location, redirectsLeft - 1, onResponse, onError);
      return;
    }

    if (statusCode !== 200) {
      res.resume();
      onError(new Error(`unexpected status ${statusCode} for ${url}`));
      return;
    }

    onResponse(res);
  });

  req.setTimeout(DOWNLOAD_TIMEOUT_MS, () => req.destroy(new Error('download timed out')));
  req.on('error', onError);
}

function main() {
  const rid = ridFor(process.platform, process.arch);
  if (!rid) {
    warn(
      `no prebuilt diagnyx binary for ${process.platform}/${process.arch}. ` +
      'Install the CLI yourself from https://github.com/nachiketg/diagnyx/releases ' +
      'and point DIAGNYX_PATH at it.'
    );
    return;
  }

  const version = require('../package.json').version;
  const url = `https://github.com/nachiketg/diagnyx/releases/download/v${version}/${assetName(rid)}`;
  const binDir = path.join(__dirname, '..', 'bin');
  const destPath = path.join(binDir, binaryName(process.platform));
  const tmpPath = `${destPath}.download`;

  download(
    url,
    MAX_REDIRECTS,
    (res) => {
      fs.mkdirSync(binDir, { recursive: true });
      const file = fs.createWriteStream(tmpPath);
      res.pipe(file);
      file.on('finish', () => {
        file.close(() => {
          fs.renameSync(tmpPath, destPath);
          if (process.platform !== 'win32') fs.chmodSync(destPath, 0o755);
          console.log(`diagnyx-node: downloaded diagnyx v${version} (${rid}) to ${destPath}`);
        });
      });
      file.on('error', (err) => fail(err, tmpPath));
    },
    (err) => fail(err, tmpPath)
  );
}

function fail(err, tmpPath) {
  try { fs.rmSync(tmpPath, { force: true }); } catch { /* best effort cleanup */ }
  warn(
    `could not download the diagnyx binary (${err.message}). ` +
    'DiagnyxLogger will fall back to DIAGNYX_PATH or PATH at call time -- ' +
    'install the CLI yourself from https://github.com/nachiketg/diagnyx/releases if needed.'
  );
}

function warn(message) {
  console.warn(`diagnyx-node: warning: ${message}`);
}

main();
