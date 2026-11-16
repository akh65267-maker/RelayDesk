# Relay Desk Roadmap

Each phase ends with something deployed and demoable. The weeks are rough estimates for part-time work.

## Phase 0: Foundation (1–2 weeks)
- Monorepo skeleton (ADR 0004), Aspire AppHost, Docker Compose for local development.
- Module template with its own schema and DbContext, plus NetArchTest boundary tests.
- CI: build, test, Testcontainers.
- Hetzner VPS provisioned, Caddy and TLS, a "hello" deploy through GitHub Actions.
- OpenTelemetry sending to Grafana Cloud.

**Done when:** a push to `main` deploys a health endpoint at your domain with traces visible.

## Phase 1: Tenancy and identity (2 weeks)
- Keycloak realm as code, Organizations, the `relay-web` and `relay-api` clients.
- Tenant signup, which creates both the Organization and the tenant row.
- Agent invites, and the Owner, Admin and Agent roles.
- `ITenantContext`, EF query filters, Postgres RLS, and tenant-isolation tests.
- Audit log module.
- Next.js app shell with login.

**Done when:** two tenants sign up and provably cannot see each other's data.

## Phase 2: Tickets and messaging backbone (2 weeks)
- Tickets: create, status, assignment, tags, internal notes, search.
- MassTransit with RabbitMQ, outbox, idempotent consumers, dead-letter queue.
- Agent inbox UI.

**Done when:** the full ticket lifecycle works and an event is emitted for each change.

## Phase 3: Real-time chat (2 weeks)
- Realtime service, SignalR hubs, Redis backplane.
- Embeddable widget script, visitor tokens, conversations that become tickets.
- Live inbox, presence and typing indicators.

**Done when:** a visitor on a demo page chats live with an agent.

## Phase 4: Knowledge base and AI (2–3 weeks)
- Knowledge-base articles (Markdown), chunking, embeddings into pgvector.
- AI worker: triage, suggested replies (hybrid RAG), summaries.
- PII redaction, usage ledger, per-tenant rate limits, fallback when AI is down.
- Eval dataset and a CI eval job.

**Done when:** an agent gets grounded, cited reply suggestions and token usage is metered.

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
