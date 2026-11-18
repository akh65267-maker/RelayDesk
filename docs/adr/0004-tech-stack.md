# ADR 0004: Technology stack

**Status:** Accepted

## Decision
| Concern | Choice |
|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core |
| Database | PostgreSQL 17 with pgvector |
| Cache, backplane, rate limits | Redis |
| Messaging | Wolverine with a PostgreSQL outbox; RabbitMQ for cross-service events (see 0006) |
| Identity | Keycloak (see 0005) |
| Frontend | Next.js (App Router, TypeScript) for the agent app; a small framework-free script for the chat widget |
| Local orchestration | .NET Aspire |
| Observability | OpenTelemetry sending to Grafana Cloud (free tier) |
| Tests | xUnit, Testcontainers, NetArchTest, Playwright (end-to-end) |
| CI/CD | GitHub Actions, GHCR |
| Repository | A single repo: `/src/api`, `/src/realtime`, `/src/ai-worker`, `/src/shared`, `/web`, `/widget`, `/deploy`, `/docs` |

## Consequences
- pgvector avoids running a separate vector database.
- Aspire gives a one-command local environment and telemetry by default.

## Alternatives considered
- **Qdrant or Pinecone:** another moving part.
- **Kafka:** overkill for this scale.
