# Retrieving Logs by Relevance

`diagnyx retrieve` finds the entries most relevant to a question — not the most recent ones, and not an exact filter match. It's the grounding step for AI-assisted analysis: given a question like "why did checkout fail last night," it hands back a small, ranked set of candidate entries for something else (a future `diagnyx ask`, or your own tooling) to reason over. There is no LLM call here and no network dependency — ranking is a deterministic, local score, so this works with no AI provider configured at all.

```bash
diagnyx retrieve --question <text> [--since <time>] [--until <time>]
                 [--source <name>] [--limit <n>]
```

| Flag | Description |
|------|-------------|
| `--question <text>` | **Required.** Free text describing what you're looking for. Drives the relevance ranking. |
| `--since <time>` | Only consider entries at or after this time. Same [time values](QUERY.md#time-values) as `diagnyx query`. |
| `--until <time>` | Only consider entries at or before this time. |
| `--source <name>` | Only consider entries from this source. Case-insensitive. |
| `--limit <n>` | Return at most `n` entries — the `n` most relevant. Default `20`. |

## How it's different from `diagnyx query`

| | `diagnyx query` | `diagnyx retrieve` |
|---|---|---|
| Selects entries by | Exact filters (`--level`, `--contains`, ...) | A candidate window (`--since`/`--until`/`--source`), then relevance |
| Orders results | Chronological, oldest first | Most relevant first |
| `--limit` keeps | The most *recent* matches | The most *relevant* matches |
| Typical use | "Show me errors from the last hour" | "What's relevant to *this* question" |

Both read through the same [`IQueryableSink`](EXTENDING_SINKS.md#making-a-sink-queryable) — `retrieve` isn't a separate storage path, it's `query`'s own filtering with a ranking step on top.

## How ranking works

Diagnyx fetches up to 500 candidate entries matching `--since`/`--until`/`--source` (reusing `diagnyx query`'s own filtering), then scores each one: the question and each entry's message + context are both broken into lowercase words (3+ letters/digits, common stop words like "the"/"and"/"was" removed), and the score is how many distinct question words also appear in that entry. Entries are sorted by score, highest first; entries that tie (including a question with no matching words at all, which scores everything `0`) fall back to most-recent-first.

This is intentionally simple — word overlap, not semantic search or embeddings — matching how the rest of Diagnyx favors small, explainable mechanisms over frameworks. It's good at "does this entry mention what the question mentions," not at synonyms, paraphrasing, or short numeric identifiers (`ord-1` vs `ord-2` both tokenize to just `ord`, since single digits are dropped as too short — name entities with enough surrounding text to stay distinguishable, e.g. `orderId ord-9921` rather than a bare number).

## Example

```bash
diagnyx retrieve --question "payment timeout during checkout" --since 2h
```

```json
{"score":3,"timestamp":"2026-09-22T15:55:49.610Z","level":"error","message":"checkout failed: payment timeout","source":"svc","context":{"orderId":"ord-2"},"traceId":null,"spanId":null}
{"score":2,"timestamp":"2026-09-22T15:55:49.500Z","level":"error","message":"payment gateway timeout","source":"svc","context":{"orderId":"ord-1"},"traceId":null,"spanId":null}
{"score":1,"timestamp":"2026-09-22T15:55:49.400Z","level":"info","message":"checkout page rendered","source":"svc","context":null,"traceId":null,"spanId":null}
```

## Output

Results are printed as [JSON Lines](https://jsonlines.org/), one entry per line, most relevant first. Each line is the [canonical seven-field schema](SCHEMA.md) plus one more: `score`, the integer relevance score described above (higher is more relevant; `0` is a recency-only fallback, not "excluded" — nothing is filtered out by score).

## Which sinks can be retrieved from

Same as [`diagnyx query`](QUERY.md#which-sinks-can-be-queried): `file`, `sqlite`, `postgres`, `mysql`, and `mssql` work; `otlp` and `loki` are export-only and fail with a clear error. `diagnyx retrieve` reads the sink from your [config](CONFIG.md) exactly as `diagnyx log` and `diagnyx query` do, including `DIAGNYX_CONFIG`.

Ranking never branches on sink type — it only ever calls the same `IQueryableSink.Query()` every queryable sink implements — so the same question against the same data produces the same ranked entries regardless of which one is active. CI runs the identical check scenario against all five to keep that true.

## Behavior notes

- **Nothing logged yet is not an error** — same as `query`: an empty candidate pool returns no output and exits `0`.
- **The file sink's rotation and reach limits are the same as `query`'s**: only the active file is read (rotated backups aren't included), and it works while another process is still appending.
- **`--limit` bounds the *final*, ranked output**, not the candidate pool fetched from the sink — the pool itself is capped at 500 regardless of `--limit`, so a very high `--limit` won't pull more than that out of a large table.
