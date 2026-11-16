# ADR 0008: AI architecture, PII redaction and metering

**Status:** Accepted

## Decision
- **Provider:** use `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) so the provider can be changed through configuration.
- **Features:**
  - **Triage:** category, priority and sentiment on `TicketCreated`.
  - **Suggested reply:** RAG over the tenant's knowledge base.
  - **Summaries:** conversation summaries.
- **RAG:**
  - Knowledge-base articles are split into chunks and embedded asynchronously.
  - Embeddings are stored in pgvector with `tenant_id` and protected by RLS.
  - Retrieval uses hybrid search (vector plus full-text).
- **GDPR:** emails, phone numbers and card-like numbers are redacted before calling the LLM. Prompts and responses are retained for 30 days by default, and tenants can opt out of AI features.
- **Cost control:**
  - Per-tenant token metering is written to a `usage` ledger.
  - Quotas are enforced through entitlements (see 0009).
  - Requests are rate-limited per tenant.
- **Quality:**
  - An eval dataset in the repo scores suggestion relevance and groundedness.
  - CI runs the evals on prompt changes.
- **Resilience:** timeouts, retries and a circuit breaker. If the AI is unavailable, the product still works without AI.

## Consequences
- AI is an enhancement, never a dependency of the core workflow.
- The evals and metering set this apart from "wrapper" projects.
