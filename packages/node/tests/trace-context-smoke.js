'use strict';

// Smoke test for wrapper-supplied trace context (DX-028): an explicit
// traceContext argument, and ambient context via withTraceContext()
// (AsyncLocalStorage), including propagation across an await.

const { DiagnyxLogger } = require('..');

const logger = new DiagnyxLogger('trace-context-smoke-test-node');

async function main() {
  let code = 0;

  code |= logger.info('explicit trace context', null, {
    traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
    spanId: '00f067aa0ba902b7',
  });

  await DiagnyxLogger.withTraceContext(
    { traceId: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', spanId: 'bbbbbbbbbbbbbbbb' },
    async () => {
      await new Promise((resolve) => setTimeout(resolve, 5));
      code |= logger.warn('ambient trace context after await');
    }
  );

  process.exit(code);
}

main();
