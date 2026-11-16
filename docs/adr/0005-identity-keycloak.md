# ADR 0005: Identity with Keycloak Organizations

**Status:** Accepted

## Decision
- **Realm and tenants:** a single realm `relay`. Each tenant is a Keycloak **Organization** (Keycloak 26+).
- **Clients:**
  - `relay-web`: a public client using PKCE.
  - `relay-api`: bearer-only.
  - `relay-backend`: a service-account client for the backend's admin operations, such as provisioning organizations.
- **Tokens and roles (revised in Phase 1):** the JWT only identifies the user (`sub`, `email`). The client picks a tenant with the `X-Tenant-Id` header, and the API checks membership and role (Owner, Admin or Agent) in the Tenancy module's database.
  - Keycloak Organizations don't support per-organization roles, and putting roles in tokens means they stay stale until the token refreshes.
  - Organizations are still kept in sync with tenants, so per-tenant SSO can be added later.
- **Invitations:** handled by the Tenancy module (hashed single-use tokens, 7-day expiry, email must match). Email delivery comes in a later phase.
- **Widget users:** anonymous visitors in the chat widget don't use Keycloak. They get a short-lived signed visitor token issued by the API and scoped to one tenant.
- **Future:** per-tenant SSO (SAML or OIDC) through Organization identity providers for the Business tier.

## Consequences
- An industry-standard OIDC setup, with SSO ready later.
- About 1 GB of extra RAM, plus realm configuration that has to be exported to the repo (`deploy/keycloak/relay-realm.json`, secrets injected through environment placeholders).

## Alternatives considered
- **A realm per tenant:** doesn't scale operationally.
- **Auth0 or Clerk:** less depth to show.
- **OpenIddict:** more custom code.
