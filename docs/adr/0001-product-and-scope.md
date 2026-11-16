# ADR 0001: Product, customer and MVP scope

**Status:** Accepted

## Context
This is a portfolio project for remote roles in the EU, Canada and Asia. It has to show B2B SaaS, real-time and AI skills without overlapping an existing e-commerce microservices project. The product will grow beyond the MVP.

## Decision
- **Product:** a multi-tenant customer-support helpdesk.
- **Customer:** small and mid-size software companies with 5–50 support agents.
- **Pricing:** Free, Pro and Business tiers, priced per seat plus a monthly AI usage quota.
- **Compliance baseline:** GDPR, because it's the strictest target market.
- **MVP:** tenant onboarding, inviting agents, RBAC (Owner, Admin, Agent); tickets (status, assignment, tags, internal notes); an embeddable chat widget and a live inbox; a knowledge base; AI suggested replies and triage; Stripe subscriptions; an audit log; GDPR data export and delete for each tenant.
- **Out of MVP:** email ingestion, SLAs, analytics, automations, mobile apps, per-tenant SSO.

## Consequences
- The three themes fit together naturally instead of being bolted on.
- Future features are added as new modules (see 0002).

## Alternatives considered
- **Project management SaaS:** AI and chat fit it less naturally.
- **FinTech:** needs domain depth.
- **WMS:** overlaps the e-commerce project.
