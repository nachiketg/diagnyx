'use strict';

const { spawnSync } = require('child_process');
const { existsSync } = require('fs');
const { platform } = require('os');
const { delimiter, join } = require('path');
const { AsyncLocalStorage } = require('async_hooks');

// Ambient trace context: set via DiagnyxLogger.withTraceContext() and picked
// up automatically by every log call made within that async scope (e.g. for
// the lifetime of one request), without threading it through call sites.
const traceContextStorage = new AsyncLocalStorage();

class DiagnyxLogger {
  constructor(source, binaryPath) {
    if (!source || typeof source !== 'string' || !source.trim()) {
      throw new Error('source must be a non-empty string');
    }
    this._source = source;
    this._binary = binaryPath || process.env.DIAGNYX_PATH || findBundled() || findInPath() || null;
  }

  debug(message, context, traceContext) { return this._log('debug', message, context, traceContext); }
  info(message, context, traceContext)  { return this._log('info',  message, context, traceContext); }
  warn(message, context, traceContext)  { return this._log('warn',  message, context, traceContext); }
  error(message, context, traceContext) { return this._log('error', message, context, traceContext); }
  fatal(message, context, traceContext) { return this._log('fatal', message, context, traceContext); }

  log(level, message, context, traceContext) { return this._log(level, message, context, traceContext); }

  // Runs fn with { traceId, spanId } as the ambient trace context for every
  // Diagnyx log call made during it (including in nested async calls).
  static withTraceContext(traceContext, fn) {
    return traceContextStorage.run(traceContext, fn);
  }

  // Resolution and spawn failures are caught here and reported as a console
  // warning rather than thrown, so a missing or broken CLI never crashes
  // the host application.
  _log(level, message, context, traceContext) {
    if (!this._binary) {
      warn(
        'diagnyx binary not found. Install it from https://github.com/nachiketg/diagnyx/releases ' +
        'or set the DIAGNYX_PATH environment variable'
      );
      return 1;
    }

    const args = ['log', '--level', level, '--message', message, '--source', this._source];

    if (context !== undefined && context !== null) {
      args.push('--context', typeof context === 'string' ? context : JSON.stringify(context));
    }

    const spawnEnv = { ...process.env };
    const traceparent = toTraceparent(traceContext || traceContextStorage.getStore());
    if (traceparent) {
      spawnEnv.TRACEPARENT = traceparent;
    }

    let result;
    try {
      result = spawnSync(this._binary, args, { stdio: ['ignore', 'inherit', 'inherit'], env: spawnEnv });
    } catch (err) {
      warn(`failed to invoke diagnyx CLI: ${err.message}`);
      return 1;
    }

    if (result.error) {
      warn(`failed to invoke diagnyx CLI: ${result.error.message}`);
      return 1;
    }

    return result.status ?? 1;
  }
}

// Builds a W3C traceparent header from { traceId, spanId }. Diagnyx Core
// validates the format on read (see core/Logging/TraceContext.cs), so this
// only needs to shape the string -- malformed input just yields a null
// traceId/spanId on the log entry rather than a thrown error.
function toTraceparent(traceContext) {
  if (!traceContext || typeof traceContext !== 'object') return null;
  const { traceId, spanId } = traceContext;
  if (typeof traceId !== 'string' || typeof spanId !== 'string') return null;
  return `00-${traceId}-${spanId}-01`;
}

function warn(message) {
  console.warn(`diagnyx: warning: ${message.replace(/\.$/, '')}. Log entry dropped.`);
}

// scripts/postinstall.js downloads a matching binary into bin/ at install
// time; this is where DiagnyxLogger looks for it.
function findBundled() {
  const binary = platform() === 'win32' ? 'diagnyx.exe' : 'diagnyx';
  const candidate = join(__dirname, 'bin', binary);
  return existsSync(candidate) ? candidate : null;
}

function findInPath() {
  const binary = platform() === 'win32' ? 'diagnyx.exe' : 'diagnyx';
  const dirs = (process.env.PATH || '').split(delimiter);
  for (const dir of dirs) {
    if (!dir) continue;
    const candidate = join(dir.trim(), binary);
    if (existsSync(candidate)) return candidate;
  }
  return null;
}

module.exports = { DiagnyxLogger };
