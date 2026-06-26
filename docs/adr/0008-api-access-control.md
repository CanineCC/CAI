# 0008 — Default-deny authorization with an open public API

- Status: Accepted
- Date: 2026-06-27

## Context

cai.canine.dev is, by design, an open read-only standard: the rubric API, the scoring endpoints and the
UI are meant to be public, gated by rate limiting rather than identity. But "public by intent" and
"unprotected by accident" look the same in code unless the default is made explicit — and the project
should be able to lock the API down (e.g. a private deployment, or partner-only access) without a rewrite.

## Decision

Authorize by default-deny, then open the public surface explicitly:

- A **fallback authorization policy** (`RequireAuthenticatedUser`) denies any endpoint that does not opt
  out, so a newly-added endpoint is closed unless deliberately made public.
- Every current endpoint is explicitly public — `[AllowAnonymous]` on each handler — because the standard
  *is* the product.
- The API additionally calls an **imperative access guard** (`ApiAccess.EnsureAllowed`, throw-on-violation)
  at each handler. It is a no-op while the API is open (the default), but setting
  `Api:RequirePartnerKey=true` flips it to partner-only: any caller that is neither loopback (the
  co-located surveyor) nor presenting a valid `X-CAI-Partner` key is denied with 403.

## Consequences

- The default flips from "open by omission" to "closed unless declared", which is the property that
  matters for a long-lived public service.
- An operator can lock the API to partners with one config switch, with the decision enforced at every
  handler rather than relying on edge configuration.
- The guard duplicates a little of the rate-limiter's partner/loopback logic; the two are kept separate
  because one decides *access* and the other decides *quota*.
- There is no identity provider: "authenticated user" is only meaningful once a scheme is added, which is
  why public endpoints are explicit `[AllowAnonymous]` and the partner-key path is the concrete control.
