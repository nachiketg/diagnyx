# Phase 2 Setup Guide: OTLP, Loki & Grafana

Phase 2 turns Diagnyx from a local structured logger into something that plugs into an existing observability stack: export logs over OTLP, push them to Grafana Loki and browse them in Grafana, or query them straight from the CLI. This guide walks through all three, with config you can copy and paste, and a local Docker sandbox if you don't already have a collector, Loki, or Grafana to point at.

Every `diagnyx.config.json` snippet, CLI command, and error message below has been verified against a real build of the CLI. The Loki and Grafana versions in the local sandbox (Step 1) are the exact images this repo's own CI runs against on every change.

## What you'll do

1. (Optional) Spin up a local OTel Collector, Loki, and Grafana with Docker Compose.
2. Export logs to the collector via [OTLP](CONFIG.md#sinkotlp).
3. Push logs to [Loki](CONFIG.md#sinkloki) and view them in Grafana with the [starter dashboard](../dashboards/README.md).
4. Search your logs from the CLI with [`diagnyx query`](QUERY.md).

## Prerequisites

- The `diagnyx` CLI installed — see the [Quick Start](../README.md#quick-start) if you haven't yet.
- Somewhere for logs to go: your own OTel collector and/or Loki instance, or Docker to run the local sandbox below.

---

## Step 1: A local sandbox (optional)

Skip this if you already have a collector, Loki, and Grafana running somewhere — jump to [Step 2](#step-2-export-logs-via-otlp) and point Diagnyx at your real endpoints instead.

To try things out locally, save this as `otelcol-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      http:
        endpoint: 0.0.0.0:4318

exporters:
  debug:
    verbosity: detailed

service:
  pipelines:
    logs:
      receivers: [otlp]
      exporters: [debug]
```

And this as `docker-compose.yml`, next to it:

```yaml
services:
  otel-collector:
    image: otel/opentelemetry-collector:latest
    command: ["--config=/etc/otelcol-config.yaml"]
    volumes:
      - ./otelcol-config.yaml:/etc/otelcol-config.yaml
    ports:
      - "4318:4318"

  loki:
    image: grafana/loki:3.0.0
    ports:
      - "3100:3100"

  grafana:
    image: grafana/grafana:11.1.0
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_USER=admin
      - GF_SECURITY_ADMIN_PASSWORD=admin
```

Then:

```bash
docker compose up -d
```

You now have an OTel Collector on `localhost:4318`, Loki on `localhost:3100`, and Grafana on `localhost:3000` (sign in with `admin` / `admin`). Loki and Grafana are pinned to the exact versions this repo's CI uses; the collector uses `:latest` since it's only forwarding OTLP to its `debug` exporter here, not exercising anything version-sensitive.

---

## Step 2: Export logs via OTLP

Point the `otlp` sink at your collector (or the local one from Step 1):

```json
{
  "sink": {
    "type": "otlp",
    "otlp": {
      "endpoint": "http://localhost:4318",
      "maxRetries": 3
    }
  }
}
```

Save that as `diagnyx.config.json`, then log something:

```bash
diagnyx log --level error --message "payment failed" --source checkout-service --context '{"orderId":"ord-9921"}'
```

If you're running the local sandbox, watch it arrive:

```bash
docker compose logs otel-collector --tail 20
```

Diagnyx sends one `LogRecord` per call, mapping `level` to `SeverityText`/`SeverityNumber`, `message` to `Body`, `source` to a `service.name` resource attribute, and `context` to structured attributes — see [`docs/OTEL_MAPPING.md`](OTEL_MAPPING.md) for the full field-by-field mapping. A collector-unreachable or 5xx failure retries with backoff before giving up; see [`sink.otlp`](CONFIG.md#sinkotlp) for the exact retry rules.

---

## Step 3: Push logs to Loki and view them in Grafana

Switch the sink to `loki`:

```json
{
  "sink": {
    "type": "loki",
    "loki": {
      "endpoint": "http://localhost:3100"
    }
  }
}
```

```bash
diagnyx log --level warn --message "cache miss" --source checkout-service
```

### Import the starter dashboard

1. In Grafana, add a **Loki** data source pointing at `http://localhost:3100` (**Connections → Data sources → Add data source**).
2. Go to **Dashboards → New → Import** and upload [`dashboards/diagnyx-logs.json`](../dashboards/diagnyx-logs.json).
3. When prompted, pick the Loki data source you just added.

You'll see log volume by level, log volume by source, and a raw log browser — full details, including the HTTP API alternative to steps 2-3, are in [`dashboards/README.md`](../dashboards/README.md).

### Explore it directly

In Grafana Explore, with the Loki data source selected:

```logql
{service_name="checkout-service"} | json
```

`| json` parses each line's fields (including `traceId`, `spanId`, and `context`) so you can filter on any of them, e.g. `{service_name="checkout-service"} | json | level="error"`.

---

## Step 4: Query your logs from the CLI

`diagnyx query` searches whichever sink is configured — by time, level, source, and text — without hand-writing SQL or grepping a file:

```bash
diagnyx query --since 15m --level error --source checkout-service
diagnyx query --contains ord-9921
```

Note the [`otlp` and `loki` sinks are export-only](QUERY.md#which-sinks-can-be-queried) — `diagnyx query` reads from `file` or a database sink. If you want your logs queryable *and* exported, see fan-out below. Full reference: [`docs/QUERY.md`](QUERY.md).

---

## Other Phase 2 features

This guide covers the OTLP/Loki/Grafana path end to end. A few more Phase 2 pieces, each documented on its own:

| Feature | What it does | Docs |
|---------|---------------|------|
| Trace context | `traceId`/`spanId` populate automatically from an active trace, and the .NET/Node SDKs accept one explicitly | [`docs/OTEL_MAPPING.md`](OTEL_MAPPING.md) |
| Multi-sink fan-out | Write to more than one sink at once — e.g. a local file *and* OTLP, so logs stay queryable and exported | [`sink.types`](CONFIG.md#sinktypes-fan-out) |
| File retention | Rotate and cap the file sink by age or size | [`sink.file`](CONFIG.md#sinkfile) |
| Database retention | Delete rows older than a configured age from any RDBMS sink | [Retention (RDBMS Sinks)](CONFIG.md#retention-rdbms-sinks) |
| Prometheus metrics | An opt-in `/metrics` endpoint with log counts by level and source | [`metrics`](CONFIG.md#metrics) |

---

## Troubleshooting

| Message | What it means | Fix |
|---------|----------------|-----|
| `OTLP export failed after N attempt(s): Connection refused` | The collector at `sink.otlp.endpoint` isn't reachable. | Confirm it's running and the port matches (`4318` by default); check `docker compose ps` if using the local sandbox. |
| `Loki export failed: Connection refused` | Same as above, for `sink.loki.endpoint` (`3100` by default). | Confirm Loki is running and reachable. |
| `... sink is write-only, so 'diagnyx query' can't read from it` | You're querying an `otlp`/`loki`-only config. | Query your collector or Loki instead (e.g. Grafana Explore), or add a queryable sink via [fan-out](CONFIG.md#sinktypes-fan-out). |
| Dashboard imports but panels are empty | No matching data yet, or the wrong Loki data source was selected on import. | Log at least one entry with the `loki` sink first; re-import and pick the right data source if needed. |
| `sink.otlp.endpoint is required to use the 'otlp' sink` | `sink.type` (or `sink.types`) includes `otlp`, but `sink.otlp.endpoint` is missing. | Add the `endpoint` field shown in [Step 2](#step-2-export-logs-via-otlp). |
