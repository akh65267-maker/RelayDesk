# ADR 0002: Modular monolith with two extracted services

**Status:** Accepted

## Context
One developer, a small VPS, and a product that will grow. Full microservices add operational cost without benefit at this scale, and microservices are already shown in another project.

## Decision
- **Core API:** a modular monolith containing the Tenancy, Identity, Tickets, Conversations, KnowledgeBase, Billing and Audit modules.
- **Extracted services:**
  - **Realtime:** SignalR. It scales differently and handles long-lived connections.
  - **AI worker:** asynchronous. It is slow, costly and fails more often.
- **Module rules:**
  - Each module has its own Postgres schema and DbContext.
  - Modules communicate only through public contracts or integration events.
  - No module touches another module's tables.
  - Boundaries are enforced with architecture tests (NetArchTest).
- **Growth path:** new features (SLAs, email, analytics, automations) become new modules subscribing to events. A module is extracted into its own service only when measurements justify it.

## Consequences
- Simple deploys and transactions inside a module.
- Discipline is enforced by tests, not by network boundaries.

## Alternatives considered
- **Full microservices:** too much operational overhead.
- **A plain layered monolith:** boundaries erode as features grow.
