'use strict';

const { DiagnyxLogger } = require('..');

const logger = new DiagnyxLogger('sdk-test-node');
const code = logger.info('node SDK smoke test', { ci: true });
process.exit(code);
