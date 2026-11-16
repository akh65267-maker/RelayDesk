# ADR 0009: Billing with Stripe and entitlements

**Status:** Accepted

## Decision
- **Stripe:** Stripe Billing in test mode, using Checkout and the Customer Portal.
- **Webhooks:**
  - Signatures are verified.
  - Events are stored in an inbox table keyed by event ID, then processed idempotently.
- **Source of truth:** our database decides what each tenant is entitled to. Stripe is an input to it.
- **Entitlements:** a module exposes `IEntitlements.Has(feature)` and `IEntitlements.Limit(name)`, with plan definitions in configuration (seats, AI tokens per month, knowledge-base article limit, feature flags). Features check entitlements, never plan names.
- **Seats:** enforced when an agent is invited. AI quota is enforced through the usage ledger.

## Consequences
- New features plug into pricing without code changes in Billing.
- Changing payment provider later doesn't affect feature code.
