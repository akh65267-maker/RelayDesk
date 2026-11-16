# ADR 0005: Identity with Keycloak Organizations

**Status:** Accepted

## Decision
- **Realm and tenants:** a single realm `relay`. Each tenant is a Keycloak **Organization** (Keycloak 26+).
- **Clients:**
  - `relay-web`: a public client using PKCE.
  - `relay-api`: bearer-only.
  - A service-account client for the backend's admin operations, such as provisioning organizations and invites.
- **Tokens:** the JWT carries the `organization` claim and roles. The API maps them to `TenantId` and a role (Owner, Admin or Agent).
- **Widget users:** anonymous visitors in the chat widget don't use Keycloak. They get a short-lived signed visitor token issued by the API and scoped to one tenant.
- **Future:** per-tenant SSO (SAML or OIDC) through Organization identity providers for the Business tier.

## Consequences
- An industry-standard OIDC setup, with SSO ready later.
- About 1 GB of extra RAM, plus realm configuration that has to be exported to the repo (`deploy/keycloak/realm.json`).

## Alternatives considered
- **A realm per tenant:** doesn't scale operationally.
- **Auth0 or Clerk:** less depth to show.
- **OpenIddict:** more custom code.
