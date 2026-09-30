# Asking Questions About Your Logs

`diagnyx ask` investigates an incident in natural language from the terminal: it retrieves the log entries most relevant to your question, sends them to a configured LLM along with the question, and prints a plain-text answer.

```bash
diagnyx ask "why did the payment service fail at 3pm?"
```

There are no flags — quote the question as one argument. (Scoping `ask` by time range and source, matching `query`/`retrieve`'s `--since`/`--until`/`--source`, is a planned addition; for now it always searches the whole configured sink.)

## Setup

`ask` needs an LLM configured — see [`llm` in `docs/CONFIG.md`](CONFIG.md#llm):

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

`baseUrl` can point at any endpoint that speaks the OpenAI-compatible chat completions shape — OpenAI itself, another hosted provider, or a self-hosted/local runtime (Ollama, LM Studio, ...). The API key is always an environment variable, never a config field, so it's never something that could end up committed inside `diagnyx.config.json`.

## How it works

1. **Retrieve.** The question is handed to the same [retrieval layer](RETRIEVAL.md) `diagnyx retrieve` uses — up to 20 log entries, ranked by relevance to the question, pulled from whichever sink is configured.
2. **Ask.** The question and the retrieved entries (numbered, with timestamp, level, source, message, and context) are sent to the configured LLM in one chat completion request, with a system message instructing it to answer only from what it was given.
3. **Print.** The model's reply is printed as plain text to stdout — nothing else, so it reads like an answer, not a log line.

No retrieval means no LLM call: if nothing relevant is found, `ask` fails with a clear message instead of spending a request on empty context.

## Example

```bash
$ diagnyx ask "why did checkout fail?"
The checkout failures were caused by a payment gateway timeout (entry 2), which
then caused the checkout request itself to fail (entry 5). Both occurred within
the same few seconds and reference order ord-2.
```

(Illustrative — the actual wording depends on your configured model and your logs.)

## Errors

| Message | Cause | Fix |
|---------|-------|-----|
| `a question is required` | No question was given. | `diagnyx ask "<question>"`. |
| `diagnyx ask takes a single question argument` | The question wasn't quoted, so the shell split it into multiple arguments. | Quote it: `diagnyx ask "why did it fail?"`. |
| `no LLM is configured` | `llm.baseUrl` and/or `llm.model` are missing from config. | Add both — see [Setup](#setup). |
| `the DIAGNYX_LLM_API_KEY environment variable is not set` | The env var isn't set in this shell. | `export DIAGNYX_LLM_API_KEY=...`. |
| `... sink is write-only, so 'diagnyx ask' can't read from it` | The configured sink is `otlp` or `loki`. | Use a queryable sink (`file`, `sqlite`, `postgres`, `mysql`, `mssql`), or [fan out](CONFIG.md#sinktypes-fan-out) to one alongside your export sink. |
| `no log entries found to answer this question` | Retrieval found nothing to send. | Check the sink actually has data; try a broader question. |
| `LLM request failed: ...` | The HTTP request itself failed (unreachable endpoint, timeout, non-2xx response) or the response wasn't in the expected shape. | Check `llm.baseUrl` is correct and reachable, and that the API key is valid. |

Like every other Diagnyx command, `ask` exits `1` on any failure and prints nothing to stdout — the natural-language answer only appears on success.
