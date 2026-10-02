# Phase 3 Setup Guide: AI-Assisted Root Cause Analysis

Phase 3 turns Diagnyx from a place to search logs into a place to ask about them: `diagnyx ask` retrieves the entries most relevant to a question, sends them to an LLM you configure, and prints an answer with the specific entries that back it up — so you can verify it instead of trusting it blindly. This guide walks through configuring a provider (hosted or local), setting up redaction so sensitive context never leaves the machine, and running your first query, with real example output at every step.

Every `diagnyx.config.json` snippet, CLI command, and output below has been verified against a real build of the CLI — the local-provider steps against a real [Ollama](https://ollama.com) instance, the same way they'd run on your machine.

## What you'll do

1. Configure an LLM provider — a hosted one, or a local one with [Ollama](#step-1-configure-an-llm-provider).
2. Set up [redaction](#step-2-set-up-redaction) so secrets and PII in your log context never reach the LLM.
3. Log a small incident and run your [first `ask` query](#step-3-run-your-first-ask-query).

## Prerequisites

- The `diagnyx` CLI installed — see the [Quick Start](../README.md#quick-start) if you haven't yet.
- An LLM to point at: an API key for a hosted provider (OpenAI or similar), or [Ollama](https://ollama.com) installed locally if you'd rather not send anything off-machine.

---

## Step 1: Configure an LLM provider

`ask` needs `llm.baseUrl` and `llm.model` in `diagnyx.config.json` — there's no default, since a generic OpenAI-compatible endpoint has no universally sensible one. Pick a hosted provider or a local one; switching between them later is just a config change, never a code change.

### Option A: A hosted provider

```json
{
  "sink": { "type": "file" },
  "llm": {
    "baseUrl": "https://api.openai.com/v1",
    "model": "gpt-4o-mini"
  }
}
```

```bash
export DIAGNYX_LLM_API_KEY=sk-...
```

The API key is always an environment variable, never a config field, so it's never something that could end up committed inside `diagnyx.config.json`.

### Option B: A local provider (Ollama)

Nothing leaves the machine this way — `ask` makes zero outbound calls to anywhere but the configured endpoint, [verified operationally in CI](ASK.md#setup), not just by reading the code.

```bash
ollama pull llama3.2
```

```json
{
  "sink": { "type": "file" },
  "llm": {
    "baseUrl": "http://localhost:11434/v1",
    "model": "llama3.2"
  }
}
```

No `DIAGNYX_LLM_API_KEY` needed — Ollama doesn't check for one, and `ask` only sends the header when a key is actually set. Full reference, including other local runtimes (LM Studio, ...): [`llm` in `docs/CONFIG.md`](CONFIG.md#llm).

---

## Step 2: Set up redaction

Log `context` can carry anything an application chooses to attach, including things that have no business reaching a third-party LLM. `llm.redactContextFields` is a denylist of context JSON key names, masked wherever they appear — case-insensitively, at any depth:

```json
{
  "sink": { "type": "file" },
  "llm": {
    "baseUrl": "http://localhost:11434/v1",
    "model": "llama3.2",
    "redactContextFields": ["customerEmail", "apiKey", "ssn"]
  }
}
```

Given a context of `{"orderId":"ord-9921","customerEmail":"jane.doe@example.com"}`, here's exactly what `ask` sends to the LLM with that config in place — captured from a real run:

```
3. [2026-10-02T12:28:29.350Z] error (checkout): payment gateway timeout after 30s context={"orderId":"ord-9921","customerEmail":"[REDACTED]"}
```

`orderId` passes through; `customerEmail` doesn't, even though nothing in `message` or the question mentioned it was sensitive — `ask` masks by key name alone. The key itself is kept, so the model still knows the field existed; only its value is gone. This is unset (nothing redacted) by default — same behavior as before Phase 3 added it — so turn it on explicitly for whichever keys matter in your logs. Full reference, including how nested objects and arrays are handled: [Redaction in `docs/ASK.md`](ASK.md#redaction).

This only affects what `ask` sends to the LLM. `diagnyx query`/`diagnyx retrieve` are local, have no network call, and still show context unredacted — if you don't want something in the log store at all, redact it at the source (your application), not here.

---

## Step 3: Run your first `ask` query

Log a small incident:

```bash
diagnyx log --level error --message "payment gateway timeout after 30s" --source checkout \
  --context '{"orderId":"ord-9921","customerEmail":"jane.doe@example.com"}'
diagnyx log --level error --message "checkout request failed: payment timeout" --source checkout \
  --context '{"orderId":"ord-9921"}'
diagnyx log --level info --message "checkout page rendered" --source checkout
```

Then ask about it:

```bash
$ diagnyx ask "why did checkout fail?"
payment gateway timeout after 30s

Cited entries:
  [2] 2026-10-02T12:28:29.363Z -- checkout request failed: payment timeout
  [3] 2026-10-02T12:28:29.350Z -- payment gateway timeout after 30s
```

(Real output from a local Ollama `llama3.2` run against the three entries above — the exact wording will vary with your model and your logs, but the shape — an answer, then the specific entries behind it — won't.)

If nothing in your sink is relevant to the question, `ask` says so plainly (`no log entries found...`) rather than spending a request on empty context, and the system prompt itself tells the model not to fill gaps with general knowledge or assert causation an entry doesn't state directly — see [Grounding in `docs/ASK.md`](ASK.md#grounding).

Prefer a page to a terminal? `diagnyx ask serve` serves the same thing as a minimal local web UI — see [Web UI in `docs/ASK.md`](ASK.md#web-ui).

---

## Other Phase 3 features

This guide covers provider setup, redaction, and a first query end to end. A few more Phase 3 pieces, each documented on its own:

| Feature | What it does | Docs |
|---------|---------------|------|
| Scoping | `--since`/`--until`/`--source` narrow retrieval, same as `diagnyx query` | [`docs/ASK.md`](ASK.md) |
| Context budget | Caps how much retrieved text is sent, chunking the overflow into one deterministic summary line instead of exceeding the model's context window | [Context budget](ASK.md#context-budget) |
| Token estimate | `--verbose` prints a rough prompt token count to stderr before the request is sent | [Token estimate](ASK.md#token-estimate) |
| Relevance ranking | `diagnyx retrieve` — the same ranking `ask` uses, with no LLM call, if you just want to see what would be sent | [`docs/RETRIEVAL.md`](RETRIEVAL.md) |

---

## Troubleshooting

| Message | What it means | Fix |
|---------|----------------|-----|
| `no LLM is configured` | Both `llm.baseUrl` and `llm.model` are missing. | Add both — see [Step 1](#step-1-configure-an-llm-provider). |
| `llm.baseUrl is missing from config` / `llm.model is missing from config` | Only one of the two is set. | Add the missing one. |
| `LLM request failed: 401 ...` (or similar) | A hosted provider rejected the request because no key (or the wrong one) was sent — `ask` doesn't require one itself, since it has no way to know which providers do. | `export DIAGNYX_LLM_API_KEY=...`; not needed at all for most local providers. |
| `... sink is write-only, so ask can't read from it` | `sink.type` is `otlp` or `loki`. | Use a queryable sink (`file`, `sqlite`, `postgres`, `mysql`, `mssql`), or [fan out](CONFIG.md#sinktypes-fan-out) to one alongside your export sink. |
| `no log entries found to answer this question` | Retrieval found nothing relevant. | Check the sink has data; try a broader question, or loosen `--since`/`--until`/`--source` if you set any. |
| `LLM request failed: Connection refused` | `llm.baseUrl` isn't reachable. | Confirm the provider (or Ollama) is actually running, and the port matches. |

See [`docs/ASK.md`](ASK.md#errors) for the complete error reference, including every `--since`/`--until`/`--source`/`--verbose` input-validation message.
