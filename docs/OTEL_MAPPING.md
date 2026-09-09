# OpenTelemetry Logs Data Model Mapping

This document maps the [Diagnyx log entry schema](SCHEMA.md) onto the [OpenTelemetry Logs Data Model](https://opentelemetry.io/docs/specs/otel/logs/data-model/), so logs can be exported as OTLP without renaming or restructuring fields.

This is a mapping specification, not an exporter — there is no `diagnyx export otlp` command yet (see [Roadmap](../README.md#roadmap), Phase 2). [scripts/verify-otel-mapping.js](../scripts/verify-otel-mapping.js) implements the mapping below to convert a sample entry and validate the result structurally against the data model, so the mapping is checked by running code rather than by reading prose alone.

---

## Field Mapping

| Diagnyx field | OTel LogRecord / Resource field | Notes |
|----------------|----------------------------------|-------|
| `timestamp` | `Timestamp` and `ObservedTimestamp` | ISO 8601 string converted to `time_unix_nano` (uint64 nanoseconds since epoch). Diagnyx doesn't distinguish event time from ingestion time, so both OTel timestamps get the same value. |
| `level` | `SeverityText` and `SeverityNumber` | See [Severity Mapping](#severity-mapping) below. |
| `message` | `Body` | Mapped as `{ "stringValue": message }`. |
| `source` | `Resource` attribute `service.name` | Attached once per `ResourceLogs` batch, not per record — this is the OTel [service semantic convention](https://opentelemetry.io/docs/specs/semconv/resource/#service) for identifying the emitting service. |
| `context` | `Attributes` | Each top-level key becomes one `KeyValue`. See [Context → Attributes](#context--attributes) below. |
| `traceId` | `TraceId` | Already a 32-character lowercase hex string — no reformatting needed (see [Trace and Span IDs](#trace-and-span-ids)). |
| `spanId` | `SpanId` | Already a 16-character lowercase hex string — no reformatting needed. |
| *(none)* | `Flags` | Not currently populated — see [Known Gap: Trace Flags](#known-gap-trace-flags). |
| *(none)* | `InstrumentationScope` | Fixed `{ name: "diagnyx", version: <VERSION file> }`; not derived from a Diagnyx field. |

---

## Severity Mapping

The OTel spec defines `SeverityNumber` as an integer from 1-24, grouped into 6 ranges of 4 (`TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`, `FATAL`), each with 4 sub-levels for finer-grained severity within a range. Diagnyx has no `trace` level and no sub-levels, so each Diagnyx level maps to the first (least-severe) number in its OTel range:

| Diagnyx `level` | `SeverityText` | `SeverityNumber` | OTel range |
|------------------|-----------------|--------------------|------------|
| `debug` | `DEBUG` | `5` | DEBUG (5-8) |
| `info` | `INFO` | `9` | INFO (9-12) |
| `warn` | `WARN` | `13` | WARN (13-16) |
| `error` | `ERROR` | `17` | ERROR (17-20) |
| `fatal` | `FATAL` | `21` | FATAL (21-24) |

---

## Context → Attributes

Diagnyx's `context` is an arbitrary JSON object; OTel `Attributes` is a list of `KeyValue` pairs where each value is a typed `AnyValue`. Each top-level key in `context` becomes one attribute, with its value converted by JSON type:

| JSON type | `AnyValue` field |
|-----------|-------------------|
| string | `stringValue` |
| integer number | `intValue` (encoded as a string, per protobuf JSON rules for 64-bit ints) |
| non-integer number | `doubleValue` |
| boolean | `boolValue` |
| array | `arrayValue` (each element converted recursively) |
| object | `kvlistValue` (each key converted recursively) |
| `null` | `stringValue: ""` |

A `null` (or omitted) `context` maps to an empty `attributes` array.

---

## Trace and Span IDs

Diagnyx's `traceId`/`spanId` fields are already lowercase hex strings in the exact format the [OTLP/JSON encoding](https://github.com/open-telemetry/opentelemetry-proto/blob/main/opentelemetry/proto/trace/v1/trace.proto) uses for these fields (protobuf `bytes` fields are base64 elsewhere in OTLP JSON, but `trace_id`/`span_id` are a documented exception, encoded as hex specifically for human readability). Since Diagnyx already validates and stores them in that shape (see [core/Logging/TraceContext.cs](../core/Logging/TraceContext.cs)), the mapping is a direct passthrough with no conversion. When `traceId`/`spanId` are `null`, both fields are simply omitted from the `LogRecord`.

## Known Gap: Trace Flags

OTel's `LogRecord.Flags` carries the W3C `traceparent` flags byte (e.g. the sampled bit). Diagnyx's [`TraceContext.TryParse`](../core/Logging/TraceContext.cs) validates that byte but does not currently retain it — the `LogEntry` schema has no `flags` field. The mapping in this document and in `scripts/verify-otel-mapping.js` sets `Flags: 0` (`FLAG_NONE`) unconditionally. Capturing and propagating the sampled bit is a candidate for a follow-up ticket if downstream OTel consumers need it.

---

## Sample OTLP Export

Converting the [`docs/SCHEMA.md` full example entry](SCHEMA.md#full-example) through this mapping produces [`docs/examples/otlp-log-export.json`](examples/otlp-log-export.json). Regenerate and re-validate it with:

```bash
node scripts/verify-otel-mapping.js < /dev/null > docs/examples/otlp-log-export.json
```

The script exits non-zero (and does not write output) if the generated `ResourceLogs` payload fails structural validation against the data model — required fields present, `severityNumber` in range 1-24, `traceId`/`spanId` well-formed hex when present, etc. You can also pipe any single Diagnyx JSON Lines entry through it directly:

```bash
echo '{"timestamp":"2025-08-25T12:00:00.000Z","level":"info","message":"App started","source":"my-service","context":null,"traceId":null,"spanId":null}' \
  | node scripts/verify-otel-mapping.js
```
