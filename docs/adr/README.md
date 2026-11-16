# Architecture Decision Records

Product codename: **Relay Desk**, a multi-tenant B2B helpdesk SaaS with real-time chat and AI assistance.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-product-and-scope.md) | Product, customer and MVP scope | Accepted |
| [0002](0002-modular-monolith.md) | Modular monolith with two extracted services | Accepted |
| [0003](0003-multi-tenancy.md) | Shared-schema multi-tenancy with PostgreSQL RLS | Accepted |
| [0004](0004-tech-stack.md) | Technology stack | Accepted |
| [0005](0005-identity-keycloak.md) | Identity with Keycloak Organizations | Accepted |
| [0006](0006-messaging-outbox.md) | Messaging with RabbitMQ, MassTransit and outbox | Accepted |
| [0007](0007-realtime-signalr.md) | Real-time with SignalR and Redis backplane | Accepted |
| [0008](0008-ai-architecture.md) | AI architecture, PII redaction and metering | Accepted |
| [0009](0009-billing-entitlements.md) | Billing with Stripe and entitlements | Accepted |
| [0010](0010-hosting-hetzner.md) | Hosting on Hetzner with Docker Compose | Accepted |

Template: Context, Decision, Consequences, Alternatives considered.
