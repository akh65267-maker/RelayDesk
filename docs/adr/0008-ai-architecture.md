# ADR 0008: AI architecture, PII redaction and metering

**Status:** Accepted (refined in Phase 4)

## Decision
- **Provider:** features depend only on `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`).
  - **Default: local Ollama** (`llama3.2:3b` for chat, `nomic-embed-text` for 768-dimension embeddings). It's free, needs no API key, and data stays on the machine.
  - A cloud provider is a change of client registration in `AddRelayAi`. Changing the embedding model also needs a migration for the vector size and a re-index.
- **Where it runs:** the AI worker, a separate host. It consumes events from its own durable RabbitMQ queue (at most 2 messages at a time, so a local model isn't flooded), connects as `relay_app` so RLS applies, and announces results through the outbox. Only the worker registers the services that need a model (`AddAiWorkerServices`).
- **Features (agents always decide):**
  - **Triage** runs automatically on `TicketCreated`: category, priority, sentiment and a reason. It's shown as a recommendation and only applied when an agent clicks.
  - **Reply drafts**, requested by an agent, use retrieval over the tenant's knowledge base. They cite numbered sources, which the UI links to the articles, and go into the reply box for editing; they're never sent automatically.
  - **Summaries**, requested by an agent, may use internal notes. Triage and replies never see internal notes.
  - One pending request per ticket and kind, so double clicks don't queue duplicate model calls.
- **Retrieval:**
  - Published articles are split into chunks along headings (about 800 characters), embedded by the worker and stored in pgvector, with an HNSW cosine index and a generated `tsvector` column.
  - Hybrid search combines the 12 nearest neighbours by vector with the 12 best full-text matches, merged with reciprocal rank fusion.
  - Several chunks of one article become one numbered source.
  - Unpublishing or deleting an article removes it from the index.
- **Personal data:** before any call, `PiiRedactor` replaces emails, phone numbers, Luhn-checked card numbers and IBANs with placeholders. The prompt forbids echoing placeholders, and the evals check for it.
- **Cost control:** every call writes tokens to `ai.usage`. A monthly per-workspace quota (`Ai:MonthlyTokenQuota`) returns 429 on new requests and marks queued work `QuotaExceeded`. Plans replace the fixed quota in Phase 5.
- **Quality:** `tests/RelayDesk.Evals` runs against the real models, the real hybrid search and the real prompts, with these gates:
  - retrieval finds the right article in the top 3 at least 75% of the time;
  - triage is at least 60% accurate;
  - reply drafts cite the right source, state its facts and contain no placeholders.

  The gates are set for the small default model. CI runs them (`ai-evals.yml`) when AI code, prompts or the dataset change.
- **Resilience:** any failure marks the suggestion `Failed` with the reason, and the agent can retry. Tickets, chat and everything else keep working without AI.

## Consequences
- AI is an enhancement, never a dependency of the core workflow, and it never acts on a customer by itself.
- The first evals run caught that the small model left out citations; one explicit example in the prompt fixed it. Prompt changes should always be checked against the evals.
- Latency on CPU is about 10 seconds for triage and 15 to 30 seconds for a reply. Results arrive live through Realtime.
- **Follow-ups:** a 30-day retention job for suggestions (Phase 6), a per-tenant AI opt-out (Phase 5 entitlements), and a rate limit per tenant per minute.
