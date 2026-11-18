# ADR 0006: Messaging with Wolverine and a transactional outbox

**Status:** Accepted (revised in Phase 2: Wolverine replaces MassTransit)

## Context
Modules must react to each other's changes (audit now; notifications, real-time and AI later) without
calling each other directly (ADR 0002), and an event must never be lost or sent for a change that was
rolled back.

MassTransit was the original choice. Since v9 it needs a commercial license, and v8, the last free
version, no longer receives updates.

## Decision
- **Library:** Wolverine (MIT licensed, actively maintained).
- **Contracts:** integration events live in `RelayDesk.Contracts`, the only assembly modules share
  besides the shared kernel. Every event carries `TenantId`, `ActorUserId` and `OccurredAt`.
- **Transactional outbox:** endpoints publish through `IDbContextOutbox<TModuleDbContext>`. Wolverine
  stores the outgoing messages in the same PostgreSQL transaction as the business change, then delivers them.
- **In-process delivery:** consumers in the API run from **durable local queues** backed by PostgreSQL,
  with a **durable inbox** that de-duplicates by message id.
- **Failures:** retries with cooldown (200 ms, 1 s, 5 s), then the message moves to
  `wolverine.wolverine_dead_letters`, where it can be inspected and replayed.
- **Tenant context:** consumers set the tenant context from the event (`TenantContext.SetFromEvent`)
  before touching data, so RLS applies to them too.
- **Storage ownership:** the MigrationService creates Wolverine's schema as the owner and grants
  `relay_app` access. The API never changes the schema.
- **RabbitMQ:** added in Phase 3, when another service (Realtime) first needs the events.
  In-process consumers don't need a broker.

## Consequences
- No lost or phantom events, and consumers are idempotent by default.
- Audit is now driven by events (first consumer), so it shares the business transaction's guarantees.
- Handler classes must end in `Handler` and be public (Wolverine generates code that calls them);
  their implementations stay internal and are resolved from the container.
- Wolverine generates handler code at runtime (`WolverineFx.RuntimeCompilation`). Pre-generating it
  for faster cold starts is an option for production.

## Alternatives considered
- **MassTransit v8:** free but unsupported (no security fixes).
- **MassTransit v9:** commercial license.
- **Own outbox on RabbitMQ.Client:** most control, but more code to build and maintain.
