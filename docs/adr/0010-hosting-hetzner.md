# ADR 0010: Hosting on Hetzner with Docker Compose

**Status:** Accepted

## Decision
- **Server:** a Hetzner CX32-class VPS (4 vCPU, 8 GB RAM) in an EU location (Germany or Finland), for GDPR residency.
- **Services:** Docker Compose runs Caddy (automatic TLS), the API, Realtime, AI worker, web, Keycloak, Postgres, Redis, RabbitMQ and an OpenTelemetry Collector.
- **CI/CD:** GitHub Actions runs tests, builds images and pushes them to GHCR, then deploys over SSH (`docker compose pull && docker compose up -d`). Migrations run as a one-off job before the app starts.
- **Security:**
  - The Hetzner firewall allows only ports 80, 443 and 22.
  - SSH is key-only, with fail2ban and unattended upgrades.
  - Secrets live in a server `.env` that is never committed.
  - Admin UIs (Keycloak admin, RabbitMQ) are reachable only through an SSH tunnel.
- **Backups:**
  - A nightly `pg_dump` goes to a Hetzner Storage Box, encrypted and kept for 14 days.
  - A restore drill is documented and tested.
- **Observability:** the OpenTelemetry Collector sends to Grafana Cloud's free tier, with uptime checks and alerts.

## Consequences
- About €7–10 a month.
- A single server is a single point of failure, which is acceptable for a demo. The path to high availability (managed Postgres, a second node) is documented.

## Alternatives considered
- **Azure Container Apps:** more cost.
- **Kubernetes:** too much operational work for one person.
