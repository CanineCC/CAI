# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the reference scorer
(`Cai.Scoring`) follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The rubric catalogs are versioned independently and by date under `rubrics/` — any change that can
move a score for unchanged evidence mints a new rubric version (see
[ADR-0004](docs/adr/0004-versioned-frozen-rubrics.md)).

## [Unreleased]

### Fixed
- **The rate limiter no longer throttles the registry's own principals** (production operability): the open API's
  anonymous per-IP budget (1/s · 3/min · 15/day) also covered registry traffic, and since Watchdog + Assay call from
  ONE LAN IP it 429ed `/api/registry/keys` and delivery GETs mid-loop. The limiter is now traffic-class aware
  (`ApiRateLimiting`): a VALID registry bearer rides a generous per-PRINCIPAL budget (600/min — a runaway-client
  fuse; the credential is the abuse control), the registry's anonymous public probes `keys` + `health` get their own
  per-IP budget (300/min — an offline-verify loop over a whole corpus cannot trip it, a flood still does), and all
  other anonymous `/api` traffic keeps the tight open-API budget — including requests with an INVALID token, which
  also throttles token guessing. Covered by a dedicated rate-limiting suite (authenticated burst past the public
  budget, exhausted-IP + credential, tampered token, offline-verify loop, flood ceiling).
- **Unconfigured registry is safe-by-default, never `500`** ([spec §2/§3.4](docs/spec/cai-registry.md)): production
  ran the default-deny fallback policy with `AddAuthentication()` registering NO scheme, so every request without
  endpoint-level `AllowAnonymous` — all of `/api/registry/*` included — threw
  `No authenticationScheme was specified` and returned `500`. The `RegistryBearer` scheme is now registered
  unconditionally (empty principal set included), so denied requests are challenged with `401`; the new public
  `GET /api/registry/health` answers `200 Healthy` / `200 Degraded` (unconfigured — publishes rejected) /
  `503 Unhealthy` (store unreachable), and `GET /api/registry/keys` stays public (empty set when unconfigured).
  Covered by a dedicated unconfigured-boot test suite (no principals, no key file).

### Added
- **The registry API** (`/api/registry`, [ADR-0010](docs/adr/0010-signed-cai-delivery-package-and-registry.md)
  addendum + [contract](docs/spec/cai-registry.md)): producer push of signed CAI-delivery packages with
  verification on ingest (versioned JSON-schema + trusted-active-key + Ed25519 over the canonical payload +
  verdict-reproduces; tampered/unsigned/schema-invalid rejected), immutable storage (idempotent identical
  re-push, `409` on same-id/different-content), consumer pull (verbatim package, metadata, list), seller→buyer
  access grants (delivery/repo scope, expiry, revoke; ungranted reads are indistinguishable `404`s), the public
  `GET /api/registry/keys` key set, and a registry-store `/health` contribution. First identity-gated surface:
  bearer-token principals with a fixed claim contract as the Keycloak seam; authenticated registry calls are
  exempt from the open-API rate budget. Storage = SQLite behind the `IRegistryStore` (Postgres) seam.
- Architecture Decision Records under `docs/adr/` and a high-level `docs/architecture.md` with C4 /
  component diagrams.
- `Cai.slnx` repository solution so Roslyn-based tooling loads the whole project graph
  ([ADR-0007](docs/adr/0007-repository-solution-file.md)).
- Observability in `Cai.Web`: OpenTelemetry tracing + metrics, a `/health` readiness check, and
  structured logging at the request boundaries.
- Resilience pipeline (timeout + retry + circuit breaker) on the outbound surveyor call.
- Security response headers (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy), secure
  antiforgery cookie flags, and inbound validation of `/api/score` + `/api/verify` payloads.
- `supply-chain` workflow: SPDX SBOM, keyless cosign signing, and SLSA build provenance; all GitHub
  Actions pinned to commit SHAs ([ADR-0006](docs/adr/0006-supply-chain-attestation.md)).

### Changed
- Deploy workflow binds to a `production` environment and gates the swap on the `/health` probe.

## [scoring-v0.1.0] — 2026-06-26

### Added
- Initial public release of `Cai.Scoring`, the deterministic reference scorer (evidence bundle → CAI
  headline + per-lens contributions), published as a NuGet package on GitHub Packages.
- Full rubric fold: category layer, architecture surface floor, quality-bar bands, coherence, and
  critical gating.
- `cai.canine.dev`: the standard's UI, the rubric + scoring JSON API, an in-browser calculator, a
  public registry view, `/llms.txt`, and a schema.org JSON-LD glossary.

[Unreleased]: https://github.com/CanineCC/CAI/compare/scoring-v0.1.0...HEAD
[scoring-v0.1.0]: https://github.com/CanineCC/CAI/releases/tag/scoring-v0.1.0
