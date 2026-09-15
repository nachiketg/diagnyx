# Grafana Dashboards

## `diagnyx-logs.json`

A starter dashboard for logs written by the [`loki` sink](../docs/CONFIG.md#sinkloki). It ships three panels:

- **Log Volume by Level** — entries per second, grouped by the `level` label.
- **Log Volume by Source** — entries per second, grouped by the `service_name` label (set from your log entries' `source` field).
- **Recent Logs** — raw log lines, each the full canonical Diagnyx JSON entry (see [`docs/SCHEMA.md`](../docs/SCHEMA.md)) — expand a line in Grafana to see `context`, `traceId`, and `spanId`.

Two dashboard variables, `service_name` and `level`, filter all three panels and default to "All".

## Importing

1. In Grafana, go to **Dashboards → New → Import**.
2. Upload `diagnyx-logs.json` (or paste its contents).
3. When prompted, select your Loki data source — the dashboard's `datasource` variable picks it up automatically; no editing of the JSON is required.
4. Click **Import**.

Or via the [HTTP API](https://grafana.com/docs/grafana/latest/developers/http_api/dashboard/#create--update-dashboard):

```bash
jq -n --slurpfile dash diagnyx-logs.json '{dashboard: $dash[0], overwrite: true}' \
  | curl -sf -u admin:admin -X POST http://localhost:3000/api/dashboards/db \
    -H "Content-Type: application/json" -d @-
```

The dashboard keeps a fixed UID (`diagnyx-logs`), so re-importing updates the same dashboard rather than creating a duplicate.
