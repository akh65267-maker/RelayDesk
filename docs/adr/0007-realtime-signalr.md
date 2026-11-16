# ADR 0007: Real-time with SignalR and Redis backplane

**Status:** Accepted

## Decision
- **Service:** a separate Realtime service hosts SignalR hubs (`/hubs/agent` and `/hubs/visitor`), with a Redis backplane so it can scale out.
- **Groups:** `tenant:{id}`, `conversation:{id}` and `agent:{id}`. Hub methods are authorized by tenant and role.
- **Message flow:** the Realtime service consumes integration events from RabbitMQ and pushes them to clients. It never writes to domain tables; writes go through the Core API.
- **Presence:** stored in Redis with TTL heartbeats. Typing indicators are ephemeral and never persisted.

## Consequences
- Core API deploys don't drop WebSocket connections.
- Delivery is at least once, so clients deduplicate by message ID.
