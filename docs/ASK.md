# Asking Questions About Your Logs

`diagnyx ask` investigates an incident in natural language from the terminal: it retrieves the log entries most relevant to your question, sends them to a configured LLM along with the question, and prints a plain-text answer followed by the entries that actually back it up — so you can verify the answer instead of trusting it blindly.

```bash
diagnyx ask "why did the payment service fail at 3pm?" [--since <time>] [--until <time>] [--source <name>] [--verbose]
```

Quote the question as one argument. `--since`, `--until`, and `--source` narrow retrieval the same way they narrow [`diagnyx query`](QUERY.md) — same [time values](QUERY.md#time-values) and [matching rules](QUERY.md#matching-rules), same errors for an invalid value or an inverted `--since`/`--until` range. `--verbose` prints an estimated prompt token count to stderr before the request is sent — see [Token estimate](#token-estimate). All flags can appear before or after the question. With no `--since`/`--until`/`--source`, `ask` searches the whole configured sink, as before.

## Setup

`ask` needs an LLM configured — see [`llm` in `docs/CONFIG.md`](CONFIG.md#llm). `baseUrl` can point at any endpoint that speaks the OpenAI-compatible chat completions shape, hosted or self-hosted/local — switching between them is purely a config change:

```json
{
  "llm": {
    "baseUrl": "https://api.openai.com/v1",
    "model": "gpt-4o-mini"
  }
}
```

```bash
export DIAGNYX_LLM_API_KEY=sk-...
```

Or, pointed at a local Ollama instead, with no API key at all:

```json
{
  "llm": {
    "baseUrl": "http://localhost:11434/v1",
    "model": "llama3.2"
  }
}
```

The API key is always an environment variable, never a config field, so it's never something that could end up committed inside `diagnyx.config.json`. It's also optional: `DIAGNYX_LLM_API_KEY` is sent as a `Bearer` token when set, and omitted entirely when not — most local/self-hosted runtimes don't check it. A hosted provider that requires one rejects the request itself if it's missing, which surfaces as `LLM request failed: ...` below.

## How it works

1. **Retrieve.** The question, plus any `--since`/`--until`/`--source`, is handed to the same [retrieval layer](RETRIEVAL.md) `diagnyx retrieve` uses — up to 20 log entries, ranked by relevance to the question, pulled from whichever sink is configured (narrowed first by time range and source, same as `diagnyx query`, if given).
2. **Ask.** The question and the retrieved entries (numbered, with timestamp, level, source, message, and context) are sent to the configured LLM in one chat completion request, up to a character budget (see [Context budget](#context-budget) below). The system message instructs it to answer only from what it was given, and to respond as `{"answer": "...", "citedEntries": [...]}` — the entry numbers it actually relied on.
3. **Print.** The answer is printed as plain text, followed by a blank line and either:
   - a `Cited entries:` list with the timestamp and a short excerpt of each entry named in `citedEntries` (entry numbers outside the retrieved range are dropped rather than trusted), or
   - `UNSUPPORTED: no log entries were cited as evidence for this answer.`, if `citedEntries` was empty, missing, or every number in it was invalid.

No retrieval means no LLM call: if nothing relevant is found, `ask` fails with a clear message instead of spending a request on empty context.

Not every OpenAI-compatible endpoint honors structured JSON output, and both fallbacks end up `UNSUPPORTED` with no citations, since an answer that can't be verified against specific entries shouldn't read as one that has been:

- If the reply isn't JSON at all (the endpoint ignored `response_format` and returned plain prose), that prose is printed as the answer — it IS the model's actual text.
- If the reply is valid JSON but doesn't match the expected shape (e.g. `"answer"` missing or `null`, as a few real small/local models have returned), `ask` prints `(the model did not return a readable answer)` rather than the raw JSON — otherwise literal `{"answer": null, ...}` syntax would show up as if it were the answer.

## Grounding

The system message doesn't just ask for an answer — it restricts the model to what the retrieved entries actually say, and rules out two specific ways an LLM tends to hallucinate a root cause:

- **Outside knowledge.** The model is told not to fall back on general or prior knowledge about what *commonly* causes a given kind of error, even when that would sound like a plausible, confident explanation. If the logs don't say it, the answer shouldn't either.
- **Correlation presented as causation.** Two errors appearing close together in time doesn't mean one caused the other — the model is instructed to describe what the entries show (e.g., "X happened, then Y happened") rather than assert a causal link no entry states directly. Asking `ask` to connect two merely-correlated entries typically gets `UNSUPPORTED`, not a confident-sounding guess.

This is prompt instruction, not a guarantee — a model can still fail to follow it. The citation list is what actually lets you verify an answer; the grounding rules just make an unverifiable, speculative answer less likely in the first place.

## Context budget

A large incident can retrieve entries whose combined text risks exceeding the model's context window. Retrieved entries are included in the prompt, in full, up to [`llm.maxContextChars`](CONFIG.md#llm) (8000 characters by default) — whatever doesn't fit is left out of the numbered list entirely, so it can never be cited, and rolled into one summary line naming the excluded entries' count, sources, and time range instead:

```
17. [2026-09-30T15:03:58.112Z] error (checkout): checkout request started
(+12 more retrieved entries not shown individually -- sources: checkout, billing;
time range: 2026-09-30T15:02:01.004Z to 2026-09-30T15:04:11.412Z. Narrow
--since/--until/--source, or ask a more specific question, to include them.)
```

This is deterministic — no second LLM call, and entries that do fit are never merged or rewritten, so a citation always points to one real, unmodified entry. At least one entry is always included in full, even if it alone exceeds the budget, so the model always has something to work with. If you hit this regularly, narrowing `--since`/`--until`/`--source` or raising `llm.maxContextChars` gets more of the incident in front of the model.

## Token estimate

```bash
$ diagnyx ask "why did checkout fail?" --verbose
estimated prompt tokens: ~251 (system message ~221, question + log entries ~30). A rough estimate (~4 chars/token) -- not an exact count, and excludes the model's reply.
The checkout failures were caused by a payment gateway timeout...
```

`--verbose` prints this one line to stderr, before the request is sent, so an unexpectedly large request is visible before it's made — not just after, when it's already been billed. stdout is unaffected either way: it's always just the answer and citations on success, nothing else.

The estimate is deliberately rough (~4 characters per token, the same rule of thumb OpenAI's own docs use) rather than an exact count from a real tokenizer, and only covers the *prompt* — the system message plus the question and log entries actually sent. It doesn't estimate the model's reply (unknowable in advance) or a dollar cost (pricing varies by provider and model, and a local/self-hosted one typically has none at all).

## Example

```bash
$ diagnyx ask "why did checkout fail?"
The checkout failures were caused by a payment gateway timeout, which then
caused the checkout request itself to fail. Both occurred within the same
few seconds and reference order ord-2.

Cited entries:
  [2] 2026-09-30T15:04:11.203Z -- payment gateway timeout after 30s
  [5] 2026-09-30T15:04:11.412Z -- checkout failed: payment timeout (order ord-2)
```

(Illustrative — the actual wording depends on your configured model and your logs.)

Scoping a question to one source, on a large log store:

```bash
diagnyx ask "why are checkouts failing?" --source checkout --since 1h
```

## Errors

| Message | Cause | Fix |
|---------|-------|-----|
| `a question is required` | No question was given. | `diagnyx ask "<question>"`. |
| `diagnyx ask takes a single question argument` | The question wasn't quoted, so the shell split it into multiple arguments. | Quote it: `diagnyx ask "why did it fail?"`. |
| `unknown option '...'` | A flag other than `--since`/`--until`/`--source`/`--verbose` was given. | Check spelling — `ask` doesn't (yet) support `--level`, `--contains`, or `--limit`. |
| `--since/--until/--source requires a non-empty value` | The flag had no value after it. | Give it one, e.g. `--since 1h`. |
| `invalid --since/--until value '...'` | The value wasn't a valid [time value](QUERY.md#time-values). | Use a relative duration (`30m`, `2h`, `7d`) or an ISO 8601 timestamp. |
| `--since must not be later than --until` | The range is inverted. | Swap or fix the two values. |
| `no LLM is configured` | Both `llm.baseUrl` and `llm.model` are missing from config. | Add both — see [Setup](#setup). |
| `llm.baseUrl is missing from config` | `llm.model` is set but `llm.baseUrl` isn't. | Add `llm.baseUrl` — see [Setup](#setup). |
| `llm.model is missing from config` | `llm.baseUrl` is set but `llm.model` isn't. | Add `llm.model` — see [Setup](#setup). |
| `... sink is write-only, so 'diagnyx ask' can't read from it` | The configured sink is `otlp` or `loki`. | Use a queryable sink (`file`, `sqlite`, `postgres`, `mysql`, `mssql`), or [fan out](CONFIG.md#sinktypes-fan-out) to one alongside your export sink. |
| `no log entries found to answer this question` | Retrieval found nothing to send — including everything being filtered out by `--since`/`--until`/`--source`. | Check the sink actually has data in that range/source; try a broader question or scope. |
| `LLM request failed: ...` | The HTTP request itself failed (unreachable endpoint, timeout, non-2xx response) or the response wasn't in the expected shape. This includes an authentication failure from a hosted provider if `DIAGNYX_LLM_API_KEY` is missing or wrong. | Check `llm.baseUrl` is correct and reachable, and that the API key (if the provider needs one) is set and valid. |

Like every other Diagnyx command, `ask` exits `1` on any failure and prints nothing to stdout — the natural-language answer only appears on success.
