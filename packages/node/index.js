'use strict';

const { spawnSync } = require('child_process');
const { existsSync } = require('fs');
const { platform } = require('os');
const { delimiter, join } = require('path');

class DiagnyxLogger {
  constructor(source, binaryPath) {
    if (!source || typeof source !== 'string' || !source.trim()) {
      throw new Error('source must be a non-empty string');
    }
    this._source = source;
    this._binary = binaryPath
      || process.env.DIAGNYX_PATH
      || findInPath()
      || (() => {
        throw new Error(
          'diagnyx binary not found. ' +
          'Install it from https://github.com/nachiketg/diagnyx/releases ' +
          'or set the DIAGNYX_PATH environment variable.'
        );
      })();
  }

  debug(message, context) { return this._log('debug', message, context); }
  info(message, context)  { return this._log('info',  message, context); }
  warn(message, context)  { return this._log('warn',  message, context); }
  error(message, context) { return this._log('error', message, context); }
  fatal(message, context) { return this._log('fatal', message, context); }

  log(level, message, context) { return this._log(level, message, context); }

  _log(level, message, context) {
    const args = ['log', '--level', level, '--message', message, '--source', this._source];

    if (context !== undefined && context !== null) {
      args.push('--context', typeof context === 'string' ? context : JSON.stringify(context));
    }

    const result = spawnSync(this._binary, args, {
      stdio: ['ignore', 'inherit', 'inherit'],
    });

    if (result.error) throw result.error;
    return result.status ?? 1;
  }
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
