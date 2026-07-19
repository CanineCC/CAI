# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the reference scorer
(`Cai.Scoring`) follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The rubric catalogs are versioned independently and by date under `rubrics/` — any change that can
move a score for unchanged evidence mints a new rubric version (see
[ADR-0004](docs/adr/0004-versioned-frozen-rubrics.md)).

## [Unreleased]

### Added
- **Anyone can now verify a signed survey, anonymously** — `POST /api/verify-delivery` plus a working tool on
  `/verify`. Signature checking previously existed only in the CLI and on registry ingest, which put it exactly where
  the person who needs it is not: the party HANDED a signed survey is who the signature is for, and the least likely
  to install tooling to use it. The endpoint reports the two claims separately and refuses to let one vouch for the
  other — authenticity (Ed25519 over the canonical payload, against the published key set) and reproducibility
  (re-folding the embedded evidence). A package that is genuinely ours but states a number its own evidence does not
  produce is reported as signed-but-not-reproducing, never as trustworthy. The response echoes the subject
  (repository, commit, rubric, issuer, key) because a valid signature attests the document, not that it describes
  the code the recipient was shown.
- **`rubric-2026.08.16` and `rubric-2026.08.17` are published.** The archive had stopped at `.15` while the engine
  had been stamping surveys `.16` and `.17`, so a freshly signed survey named a rubric the public could not fetch and
  its recipient could not verify it. Both were generated from the engine commits that actually set
  `RubricVersion.Current` to each version, not written by hand: `.16` adds D40–D42 (runtime hardening), `.17` adds
  `scoringPolarity` metadata — matching the documented bump notes exactly.

### Changed
- **The archive now serves only catalogs it can attest.** `RubricCatalogStore` enforces that a catalog's declared
  `rubricVersion` matches the directory it is published under; mismatched or unparseable catalogs are withheld from
  `Versions()`/`Get()` and reported by the new `UnattestedVersions()` so the gap is visible rather than silent.
- **`Cai.Scoring` and `Cai.Delivery` are published to nuget.org at 0.1.3**, with GitHub Packages kept as a mirror.
  The shipped scorer was never actually public: consumers used `0.1.3-ws-e` vendored as a file, while the only
  published artifact was `0.1.0` on GitHub Packages — which requires a GitHub account even for public feeds, so
  "read our algorithm and check our number" was not true for an anonymous third party. `Cai.Delivery` had no publish
  pipeline at all. Requires the `NUGET_API_KEY` repository secret; without it the workflow warns and publishes only
  the mirror rather than failing.

### Fixed
- **Both packages now declare Apache-2.0**, matching the repository `LICENSE`, and ship the licence text inside the
  package. They previously declared MIT in `PackageLicenseExpression` — a standard whose reference implementation
  carries contradictory licence metadata is not credibly open.
- **`cai sign` emitted an invalid RFC 3339 `issuedAt`** on any machine whose locale does not use `:` as the time
  separator. `':'` in a custom format string is the *current culture's* time separator, so on a Danish-locale box it
  produced `2026-07-19T09.28.09Z` — dots for colons — baked into the signed payload, where it cannot be corrected
  without invalidating the signature. Now formatted with `InvariantCulture`, with a regression test that pins the
  behaviour under a hostile locale.

### Withheld
- **`rubric-2026.08.13` is withdrawn from publication** to `rubrics/_unattested/`. The catalog published under that
  name declares itself `rubric-2026.08.14` and is not a copy of `.14`'s file, so its provenance is unknown; it had
  been served that way since `191649a`. Relabelling it would assert provenance we do not have. The commit that set
  `Current` to `.13` predates the engine's move into the kennel repository and was not available when this was
  found — `rubrics/_unattested/README.md` records the recovery procedure.
- **The rate limiter no longer throttles the registry's own principals** (production operability): the open API's
  anonymous per-IP budget (1/s · 3/min · 15/day) also covered registry traffic, and since Watchdog + Assay call from
  ONE LAN IP it 429ed `/api/registry/keys` and delivery GETs mid-loop. The limiter is now traffic-class aware
  (`ApiRateLimiting`): a VALID registry bearer rides a generous per-PRINCIPAL budget (600/min — a runaway-client
  fuse; the credential is the abuse control), the registry's anonymous public probes `keys` + `health` get their own
  per-IP budget (300/min — an offline-verify loop over a whole corpus cannot trip it, a flood still does), and all
  other anonymous `/api` traffic keeps the tight open-API budget — including requests with an INVALID token, which
  also throttles token guessing. Covered by a dedicated rate-limiting suite (authenticated burst past the public
  budget, exhausted-IP + credential, tampered token, offline-verify loop, flood ceiling).
- **`/api/auth/session` and `/api/auth/signin` fail closed and clean, never `500`**: the prod smoke probed these
  paths (there is no interactive sign-in surface on this host) and the deployed build answered `500` — the same
  family as the unconfigured-registry bug below: unmatched paths fall to the default-deny fallback policy, whose
  challenge threw while the bearer scheme was registered conditionally. With the scheme unconditional they answer
  `401` + `WWW-Authenticate: Bearer` + a JSON error (an authenticated probe gets an honest `404`); a dedicated test
  suite now pins that contract for the `/api/auth/*` family on both the zero-config boot and the configured app.
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
