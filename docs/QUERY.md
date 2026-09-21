# Querying Logs

`diagnyx query` searches the entries in your configured sink — by time range, level, source, and text — without hand-writing SQL or grepping a file. It works the same way against the file sink and every database sink.

```bash
diagnyx query [--since <time>] [--until <time>] [--level <level>]
              [--source <name>] [--contains <text>] [--limit <n>]
```

All flags are optional and combine with AND. With no flags, it returns the 100 most recent entries.

| Flag | Description |
|------|-------------|
| `--since <time>` | Only entries at or after this time. See [Time values](#time-values). |
| `--until <time>` | Only entries at or before this time. |
| `--level <level>` | Only entries with this level: `debug`, `info`, `warn`, `error`, or `fatal`. |
| `--source <name>` | Only entries from this source. Case-insensitive. |
| `--contains <text>` | Only entries whose message **or context** contains this text. Case-insensitive. |
| `--limit <n>` | Return at most `n` entries — the most recent `n` that match. Default `100`. |

## Examples

```bash
# Errors from the last 15 minutes
diagnyx query --since 15m --level error

# Timeouts from one service, most recent 20
diagnyx query --source payment-service --contains timeout --limit 20

# Find an order anywhere in the context
diagnyx query --contains ord-9921

# A specific day, message text only
diagnyx query --since 2026-09-01 --until 2026-09-02 | jq -r '.message'
```

## Time values

`--since` and `--until` accept either form:

- **Relative** — how long before now: a whole number plus a unit, e.g. `30s`, `5m`, `2h`, `7d`, `2w` (seconds, minutes, hours, days, weeks).
- **Absolute** — an ISO 8601 timestamp: `2026-09-21`, `2026-09-21T10:30Z`, `2026-09-21T10:30:00Z`, `2026-09-21T10:30:00.123Z`, or with an offset such as `2026-09-21T10:30:00+02:00`. A value with no offset is treated as UTC, and a bare date means midnight UTC.

Both bounds are inclusive. Diagnyx stores timestamps in UTC, so all comparisons are in UTC regardless of your local time zone. Locale-style dates such as `09/21/2026` are rejected as ambiguous.

## Matching rules

- **`--level`** is an exact match.
- **`--source`** is an exact, case-insensitive match (`--source API` finds `api`).
- **`--contains`** is a case-insensitive substring match against the message or the JSON-encoded context, so `--contains ord-9921` finds an entry whose context includes `{"orderId":"ord-9921"}`. Characters like `%`, `_`, and `[` are matched literally, not as wildcards.

## Output

Results are printed as [JSON Lines](https://jsonlines.org/), one entry per line, in exactly the [canonical schema](SCHEMA.md) — whichever sink they came from — so they pipe straight into `jq` and friends. They are ordered **oldest first**; when more entries match than `--limit` allows, the *most recent* ones are kept.

## Which sinks can be queried

| `sink.type` | Queryable |
|-------------|-----------|
| `file`, `sqlite`, `postgres`, `mysql`, `mssql` | Yes |
| `otlp`, `loki` | No — these are export-only. `diagnyx query` fails with a clear error; query your collector or Loki (e.g. in Grafana Explore) instead. |

`diagnyx query` reads the sink from your [config](CONFIG.md) exactly as `diagnyx log` does, including `DIAGNYX_CONFIG`.

## Behavior notes

- **Nothing logged yet is not an error.** A missing log file or empty database returns no output and exits `0`. No matches also exits `0`; exit `1` means the query itself couldn't run (bad flag, unreachable database, export-only sink).
- **Database sinks** run the filters in SQL, so only matching rows are transferred — safe to point at a large table.
- **The file sink** streams the log file, so it works while another process is still appending to it. Lines that aren't valid Diagnyx entries are skipped, and a single `warning:` line reports how many.
