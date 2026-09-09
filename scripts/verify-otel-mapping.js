#!/usr/bin/env node
'use strict';

// Converts a Diagnyx JSON Lines log entry (docs/SCHEMA.md) into an OTLP/JSON
// logs export per the mapping documented in docs/OTEL_MAPPING.md, and
// structurally validates the result against the OpenTelemetry Logs Data
// Model (https://opentelemetry.io/docs/specs/otel/logs/data-model/).
//
// Usage:
//   node scripts/verify-otel-mapping.js               # uses the built-in sample entry
//   node scripts/verify-otel-mapping.js < entry.json   # converts one JSON Lines entry from stdin

const fs = require('fs');
const path = require('path');

const SEVERITY_NUMBER = {
  debug: 5,   // DEBUG
  info: 9,    // INFO
  warn: 13,   // WARN
  error: 17,  // ERROR
  fatal: 21,  // FATAL
};

const SEVERITY_TEXT = {
  debug: 'DEBUG',
  info: 'INFO',
  warn: 'WARN',
  error: 'ERROR',
  fatal: 'FATAL',
};

// The docs/SCHEMA.md "Full Example" entry, reused here so the mapping doc's
// sample export is generated from the exact same source data.
const SAMPLE_ENTRY = {
  timestamp: '2025-08-25T12:34:56.789Z',
  level: 'error',
  message: 'Payment processing failed',
  source: 'payment-service',
  context: { orderId: 'ord-9921', amount: 49.99, currency: 'USD', retryCount: 3 },
  traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
  spanId: '00f067aa0ba902b7',
};

function toAnyValue(value) {
  if (value === null || value === undefined) return { stringValue: '' };
  if (typeof value === 'string') return { stringValue: value };
  if (typeof value === 'boolean') return { boolValue: value };
  if (typeof value === 'number') {
    return Number.isInteger(value) ? { intValue: String(value) } : { doubleValue: value };
  }
  if (Array.isArray(value)) return { arrayValue: { values: value.map(toAnyValue) } };
  if (typeof value === 'object') {
    return { kvlistValue: { values: toKeyValueList(value) } };
  }
  throw new Error(`unsupported context value type: ${typeof value}`);
}

function toKeyValueList(obj) {
  return Object.entries(obj).map(([key, value]) => ({ key, value: toAnyValue(value) }));
}

function toUnixNano(isoTimestamp) {
  const ms = Date.parse(isoTimestamp);
  if (Number.isNaN(ms)) throw new Error(`invalid timestamp: ${isoTimestamp}`);
  return String(BigInt(ms) * 1000000n);
}

function toLogRecord(entry) {
  const level = entry.level.toLowerCase();
  const severityNumber = SEVERITY_NUMBER[level];
  if (!severityNumber) throw new Error(`unknown level: ${entry.level}`);

  const timeUnixNano = toUnixNano(entry.timestamp);

  const record = {
    // Diagnyx does not distinguish event time from ingestion time, so both
    // OTel timestamps map to the same value.
    timeUnixNano,
    observedTimeUnixNano: timeUnixNano,
    severityNumber,
    severityText: SEVERITY_TEXT[level],
    body: { stringValue: entry.message },
    attributes: entry.context ? toKeyValueList(entry.context) : [],
    droppedAttributesCount: 0,
    // Diagnyx does not currently retain the W3C traceparent flags byte
    // (see core/Logging/TraceContext.cs) -- defaults to FLAG_NONE.
    flags: 0,
  };

  if (entry.traceId) record.traceId = entry.traceId;
  if (entry.spanId) record.spanId = entry.spanId;

  return record;
}

function toOtlpExport(entry) {
  const version = fs.readFileSync(path.join(__dirname, '..', 'VERSION'), 'utf8').trim();

  return {
    resourceLogs: [
      {
        resource: {
          attributes: [{ key: 'service.name', value: { stringValue: entry.source } }],
        },
        scopeLogs: [
          {
            scope: { name: 'diagnyx', version },
            logRecords: [toLogRecord(entry)],
          },
        ],
      },
    ],
  };
}

function assert(condition, message) {
  if (!condition) throw new Error(`OTLP mapping validation failed: ${message}`);
}

// Structural checks against the OTel Logs Data Model -- not a full schema
// validator, but enough to catch a mapping bug (wrong types, out-of-range
// severity, malformed trace/span ids, missing required fields).
function validate(exportPayload) {
  const resourceLogs = exportPayload.resourceLogs;
  assert(Array.isArray(resourceLogs) && resourceLogs.length === 1, 'resourceLogs must contain exactly one entry');

  const resource = resourceLogs[0].resource;
  assert(resource && Array.isArray(resource.attributes), 'resource.attributes missing');
  const serviceName = resource.attributes.find((a) => a.key === 'service.name');
  assert(
    serviceName && typeof serviceName.value.stringValue === 'string' && serviceName.value.stringValue.length > 0,
    'resource service.name attribute missing or empty'
  );

  const scopeLogs = resourceLogs[0].scopeLogs;
  assert(Array.isArray(scopeLogs) && scopeLogs.length === 1, 'scopeLogs must contain exactly one entry');

  const records = scopeLogs[0].logRecords;
  assert(Array.isArray(records) && records.length === 1, 'logRecords must contain exactly one entry');

  const record = records[0];
  assert(/^\d+$/.test(record.timeUnixNano), 'timeUnixNano must be a numeric string');
  assert(/^\d+$/.test(record.observedTimeUnixNano), 'observedTimeUnixNano must be a numeric string');
  assert(
    Number.isInteger(record.severityNumber) && record.severityNumber >= 1 && record.severityNumber <= 24,
    'severityNumber must be an integer in range 1-24'
  );
  assert(typeof record.severityText === 'string' && record.severityText.length > 0, 'severityText missing');
  assert(record.body && typeof record.body.stringValue === 'string', 'body.stringValue missing');
  assert(Array.isArray(record.attributes), 'attributes must be an array');

  if (record.traceId !== undefined) assert(/^[0-9a-f]{32}$/.test(record.traceId), 'traceId must be 32 lowercase hex chars');
  if (record.spanId !== undefined) assert(/^[0-9a-f]{16}$/.test(record.spanId), 'spanId must be 16 lowercase hex chars');
}

function readEntry() {
  if (process.stdin.isTTY) return SAMPLE_ENTRY;

  const raw = fs.readFileSync(0, 'utf8').trim();
  return raw ? JSON.parse(raw.split('\n')[0]) : SAMPLE_ENTRY;
}

function main() {
  const entry = readEntry();
  const otlpExport = toOtlpExport(entry);

  validate(otlpExport);

  process.stdout.write(JSON.stringify(otlpExport, null, 2) + '\n');
  console.error('OK: mapping validated against the OpenTelemetry Logs Data Model.');
}

main();
