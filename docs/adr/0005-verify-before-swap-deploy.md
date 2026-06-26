# 0005 — Verify-before-swap in-place deploy with health-check rollback

- Status: Accepted
- Date: 2026-06-26

## Context

cai.canine.dev runs as a single systemd service (`cai-web.service`) on a self-hosted host
(canine-wrx1), behind nginx that terminates TLS. It is a small, low-traffic standard site, not a
fleet — a full orchestrator (Kubernetes, blue/green load balancers) would be disproportionate. We
still want deploys that cannot leave the site broken.

## Decision

Deploy via a GitHub Actions workflow on a self-hosted runner that **verifies before it swaps**:

1. Build (Release) and run the scorer tests. Any failure here is a safe no-op — the old service
   keeps serving.
2. Publish into a staging directory, then atomically swap it into place and bounce the service,
   keeping the previous build as `app.prev`.
3. Poll the `/health` readiness endpoint; if the new app does not come healthy, roll back to
   `app.prev` and restart.

The job binds to a `production` deployment environment so the run is traceable and can later be
gated behind required reviewers.

## Consequences

- A broken build never reaches production; a build that starts but fails readiness self-heals via
  rollback.
- Downtime per deploy is ~1s (a service bounce), acceptable for this site.
- There is no horizontal scaling or zero-downtime guarantee — this is deliberate for a single-host
  standard site and would be revisited if traffic or availability requirements grew.
- The readiness gate depends on the app exposing a real `/health` check (see the observability
  wiring); a deploy is only as safe as that probe is honest.
