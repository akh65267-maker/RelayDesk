# ADR 0006: Messaging with RabbitMQ, MassTransit and outbox

**Status:** Accepted

## Decision
- **Publishing:** integration events (`TicketCreated`, `MessageReceived`, `SubscriptionChanged`, and so on) are published through the MassTransit **transactional outbox** in Postgres.
- **Consuming:** consumers are idempotent, using an inbox or deduplication by message ID.
- **Tenant context:** every message carries a `TenantId` header. Consumers set the tenant context before touching data.
- **Failures:** retries with backoff, then a dead-letter queue, with an alert on DLQ depth.

## Consequences
- No lost or phantom events when the database and broker fall out of step.
- Future modules extend the system by subscribing to events.
