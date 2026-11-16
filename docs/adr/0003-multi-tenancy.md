# ADR 0003: Shared-schema multi-tenancy with PostgreSQL RLS

**Status:** Accepted

## Context
Many small tenants on one VPS. A tenant data leak is the worst possible failure.

## Decision
- **Tables:** one database and shared tables. Every tenant-owned row has `tenant_id`.
- **Layer 1:** EF Core global query filters, using `ITenantContext` resolved from the JWT.
- **Layer 2:** PostgreSQL row-level security (RLS) policies driven by `SET app.tenant_id` per connection or transaction. The application database role is not the table owner and has no `BYPASSRLS`.
- **Region:** each tenant record stores `region` (only `eu` for now) for future data-residency routing.
- **Testing:** integration tests (Testcontainers) prove that tenant A cannot read or write tenant B's data, both through the API and through raw SQL.
- **Future:** the Business tier can get a dedicated database through a tenant to connection-string resolver. This is documented but not built.

## Consequences
- Cheap, and easy to migrate.
- Defense in depth: a bug in one layer doesn't leak data.
- Background jobs must explicitly set the tenant context.

## Alternatives considered
- **Database per tenant:** expensive and gives migration sprawl.
- **Schema per tenant:** migration complexity without much benefit.
