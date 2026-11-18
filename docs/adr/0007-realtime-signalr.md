# ADR 0007: Real-time with SignalR and Redis backplane

**Status:** Accepted (refined in Phase 3)

## Decision
- **Service:** a separate Realtime service hosts two SignalR hubs, with a Redis backplane so it can scale out:
  - `/hubs/agent` for agents, authenticated with Keycloak tokens;
  - `/hubs/visitor` for chat-widget visitors, authenticated with visitor tokens (ADR 0005).
- **Groups:** every group name includes the tenant: `tenant:{tenant}`, `ticket:{tenant}:{ticket}` and `visitor:{tenant}:{visitor}`.
  A connection only joins a group after a check:
  - agents: the API confirms workspace membership, using the agent's own token;
  - visitors: the API confirms the conversation belongs to them, using the visitor's token.
- **Groups are per hub.** Agents and visitors share a ticket's typing indicators, so each hub sends to the other hub's group of the same name through `IHubContext`.
- **Message flow:** the API publishes integration events to the `relay.events` RabbitMQ exchange through its outbox (ADR 0006). The Realtime service binds its own queue and pushes notifications.
- **Pushes carry ids only** (`ticketChanged`, `conversationChanged`). Clients re-fetch through the API, so what each person sees is still decided by the API and RLS. For example, visitors never learn about internal notes.
- **No domain data:** the Realtime service never reads or writes domain tables.
- **Presence:** a per-workspace counter of connections per agent, kept in Redis. Typing indicators are ephemeral and never stored.
- **Widget transport:** the widget speaks SignalR's JSON protocol over a plain WebSocket, without negotiation, so it needs no client library.

## Consequences
- Core API deploys don't drop WebSocket connections.
- Pushes are best effort: a missed push is recovered by re-fetching, which the agent page also does after reconnecting. That's why the Realtime queue doesn't need a durable inbox.
- Membership checks cost one API call per join, not per message.
- Presence counts can drift if an instance crashes without running disconnect handlers. Adding expiry or heartbeats is a follow-up.
