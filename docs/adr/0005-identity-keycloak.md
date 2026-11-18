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
- **Widget users:** anonymous visitors in the chat widget don't use Keycloak. They get a short-lived signed visitor token issued by the API and scoped to one tenant (built in Phase 3):
  - an HS256 JWT valid for 12 hours, carrying the tenant and a random visitor id, plus the name and email if the visitor gave them;
  - signed with a key shared by the API and the Realtime service (`Widget:SigningKey`);
  - validated by its own `Visitor` scheme, which only the widget endpoints and the visitor hub accept. Keycloak stays the default scheme, so a visitor token never reaches agent endpoints, and an agent token never reaches widget endpoints;
  - the workspace is found by its public ID through a narrow `SECURITY DEFINER` function that returns only the tenant id, because RLS hides tenants.
- **Future:** per-tenant SSO (SAML or OIDC) through Organization identity providers for the Business tier.

## Consequences
- An industry-standard OIDC setup, with SSO ready later.
- About 1 GB of extra RAM, plus realm configuration that has to be exported to the repo (`deploy/keycloak/relay-realm.json`, secrets injected through environment placeholders).

## Alternatives considered
- **A realm per tenant:** doesn't scale operationally.
- **Auth0 or Clerk:** less depth to show.
- **OpenIddict:** more custom code.
