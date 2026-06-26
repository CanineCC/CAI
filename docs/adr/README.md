# Architecture Decision Records

This directory records the significant architecture decisions behind CAI — the Codebase
Assurance Index — using lightweight [ADRs](https://adr.github.io/). Each record captures the
**context** (the forces in play), the **decision** taken, and its **consequences** (the trade-offs
we accepted), so a future maintainer can understand *why* the system is shaped the way it is, not
just *what* it does.

ADRs are immutable once accepted. When a decision is revisited, add a new ADR that supersedes the
old one rather than editing history.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-deterministic-reproducible-scoring.md) | Deterministic, reproducible open scoring | Accepted |
| [0003](0003-free-paid-firewall.md) | The free/paid firewall: open standard vs surveyor judgment | Accepted |
| [0004](0004-versioned-frozen-rubrics.md) | Versioned, frozen rubric catalogs | Accepted |
| [0005](0005-verify-before-swap-deploy.md) | Verify-before-swap in-place deploy with health-check rollback | Accepted |
| [0006](0006-supply-chain-attestation.md) | Supply-chain attestation for build artifacts | Accepted |
| [0007](0007-repository-solution-file.md) | A repository solution file for whole-graph analysis | Accepted |

See [../architecture.md](../architecture.md) for the high-level shape these decisions produce.
