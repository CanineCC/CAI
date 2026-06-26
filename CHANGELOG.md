# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the reference scorer
(`Cai.Scoring`) follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The rubric catalogs are versioned independently and by date under `rubrics/` — any change that can
move a score for unchanged evidence mints a new rubric version (see
[ADR-0004](docs/adr/0004-versioned-frozen-rubrics.md)).

## [Unreleased]

### Added
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
