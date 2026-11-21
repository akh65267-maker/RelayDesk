# Relay Desk Roadmap

Each phase ends with something deployed and demoable. The weeks are rough estimates for part-time work.

## Phase 0: Foundation (1–2 weeks)
- Monorepo skeleton (ADR 0004), Aspire AppHost, Docker Compose for local development.
- Module template with its own schema and DbContext, plus NetArchTest boundary tests.
- CI: build, test, Testcontainers.
- Hetzner VPS provisioned, Caddy and TLS, a "hello" deploy through GitHub Actions.
- OpenTelemetry sending to Grafana Cloud.

**Done when:** a push to `main` deploys a health endpoint at your domain with traces visible.

## Phase 1: Tenancy and identity (2 weeks) - built, see notes below
- Keycloak realm as code, Organizations, the `relay-web` and `relay-api` clients.
- Tenant signup, which creates both the Organization and the tenant row.
- Agent invites, and the Owner, Admin and Agent roles.
- `ITenantContext`, EF query filters, Postgres RLS, and tenant-isolation tests.
- Audit log module.
- Next.js app shell with login.

**Done when:** two tenants sign up and provably cannot see each other's data.

## Phase 2: Tickets and messaging backbone (2 weeks) - built
- Tickets: create, status, assignment, tags, internal notes, search.
- Wolverine with a PostgreSQL outbox, durable inbox (idempotent consumers) and dead-letter storage (ADR 0006, revised from MassTransit).
- Agent inbox UI.

**Done when:** the full ticket lifecycle works and an event is emitted for each change.

**Notes:**
- **Built:** Tickets module (per-tenant numbering, statuses New/Open/Pending/Solved/Closed, priorities, assignment checked against membership, tags, replies and internal notes, filters and search) with RLS; `RelayDesk.Contracts` with integration events; Tenancy and Tickets publish through the outbox; the Audit module records every event from durable local queues; inbox, new-ticket and ticket pages in the web app.
- **Tests:** ticket lifecycle, an audit entry for every change, assignment rules, validation and tenant isolation (API and raw SQL).
- **Moved to Phase 3:** RabbitMQ, which is first needed when the Realtime service consumes events.

## Phase 3: Real-time chat (2 weeks) - built
- Realtime service, SignalR hubs, Redis backplane.
- Embeddable widget script, visitor tokens, conversations that become tickets.
- Live inbox, presence and typing indicators.

**Done when:** a visitor on a demo page chats live with an agent.

**Notes:**
- **Built:**
  - the widget API (visitor sessions and conversations) and visitor tokens;
  - events published to RabbitMQ (`relay.events`) alongside the local queue for Audit;
  - the Realtime service: agent and visitor hubs, Redis backplane, presence, typing, and ids-only pushes;
  - a dependency-free widget script and a demo store at `/demo/<workspace>`;
  - a live agent inbox and ticket page.
- **Choices:** a plain-JavaScript widget; a ticket is created on the visitor's first message; visitors are anonymous by default, with optional name and email.
- **Tests:** 6 widget integration tests (internal notes hidden, own conversations only, reopening, auth schemes, CORS). Checked end to end in the browser: a visitor chat appeared live in the inbox, typing worked both ways, and replies arrived live on both sides.
- **Follow-ups:** presence expiry after crashes; rate limiting on the anonymous session endpoint; email notifications for offline visitors.

## Phase 4: Knowledge base and AI (2–3 weeks) - built
- Knowledge-base articles (Markdown), chunking, embeddings into pgvector.
- AI worker: triage, suggested replies (hybrid RAG), summaries.
- PII redaction, usage ledger, per-tenant rate limits, fallback when AI is down.
- Eval dataset and a CI eval job.

**Done when:** an agent gets grounded, cited reply suggestions and token usage is metered.

**Notes:**
- **Built:**
  - KnowledgeBase module: articles, chunking, pgvector with HNSW, hybrid search;
  - AI module: automatic triage, reply drafts with cited sources, summaries, usage ledger and monthly quota;
  - AI worker with a durable queue and model bootstrapping;
  - Ollama in the AppHost;
  - knowledge-base pages and the AI assistant panel on tickets.
- **Choice:** local Ollama by default (no key, no cost, data stays local), behind `Microsoft.Extensions.AI`.
- **Tests:**
  - 16 unit tests (redaction, chunking, triage parsing);
  - 5 integration tests with a fake model on real Postgres (grounding, redaction, quota, isolation);
  - 3 evals against the real models: retrieval hit@3 100%, triage 83%, replies 3/3 cited.
- **Checked in the browser:** a published article was indexed by the worker; a widget chat was triaged automatically in about 9 seconds; a requested draft arrived live with correct facts and linked sources.
- **Follow-ups:** a suggestion retention job (Phase 6), per-tenant AI opt-out and rate limits (Phase 5).

## Phase 5: Billing and entitlements (1–2 weeks)
- Plans in configuration, `IEntitlements`.
- Stripe Checkout, Customer Portal, webhook inbox.
- Seat and AI quota enforcement, upgrade and downgrade flows.

**Done when:** upgrading in Stripe test mode unlocks features immediately.

## Phase 6: GDPR and production polish (1–2 weeks)
- Per-tenant data export (ZIP of JSON) and delete (a soft-delete window, then purge).
- Retention jobs for AI logs.
- Backups with a tested restore drill, alerts, rate limiting, security headers.
- Playwright end-to-end suite, seeded demo tenant, public demo login.
- README: architecture diagram, ADR links, screenshots, a short demo video.

**Done when:** the project is ready for your resume.

## Future modules (after MVP)
Each one is a new module subscribing to existing events (ADR 0002), gated by entitlements (ADR 0009).
- Email ingestion (inbound email to ticket)
- SLAs and business hours
- Analytics dashboards (CSAT, response times)
- Automations and rules engine
- Outbound webhooks and a public API with API keys
- Per-tenant SSO (Keycloak identity providers, Business tier)
- Multi-region data residency (ADR 0003, `region`)
- AI agent that resolves simple tickets on its own

## Phase 1 notes
- **Built:** Keycloak realm as code, tenant signup that also creates the Keycloak Organization, invitations, the Owner, Admin and Agent roles, `ITenantContext`, EF query filters, Postgres RLS (with the API connecting as `relay_app`), an append-only audit log, a migration service, and a Next.js shell (login, onboarding, team page, accepting invites).
- **Tests:** tenant-isolation integration tests at the API level and the raw-SQL level (`tests/RelayDesk.IntegrationTests`). They need Docker and run in CI.
- **Deferred:** adding Keycloak and the web app to the production Compose file (with the Hetzner deploy), invitation emails, and removing members or changing roles.
