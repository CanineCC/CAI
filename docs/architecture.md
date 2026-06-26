# CAI — Architecture

CAI (the Codebase Assurance Index) is an open, reproducible 0–100 standard for the health of a
.NET codebase: **same evidence in, same score out**. This document sketches the high-level shape;
the decisions behind it are recorded as [ADRs](adr/README.md).

## Context

```mermaid
C4Context
    title CAI — system context
    Person(consumer, "Developer / Agent", "Reads the standard, scores or verifies an evidence bundle")
    System(cai, "cai.canine.dev", "The open standard: rubric API + UI + reference scorer")
    System_Ext(surveyor, "watchdog.canine.dev", "Independent surveyor — signed surveys & advisory deductions (paid)")
    Rel(consumer, cai, "Reads spec; POSTs evidence to /api/score, /api/verify")
    Rel(cai, surveyor, "Fetches PUBLIC aggregate scan stats (best-effort, server-side)")
    Rel(surveyor, cai, "Reproduces scores against the open rubric")
```

The free/paid firewall (the deterministic measurement is open; the advisory survey is the
surveyor's product) is the defining boundary — see [ADR-0003](adr/0003-free-paid-firewall.md).

## Components

```mermaid
flowchart TD
    subgraph repo["CanineCC/CAI"]
        rubrics["rubrics/<br/>versioned, frozen catalogs"]
        scoring["Cai.Scoring<br/>(library) — deterministic OWA fold"]
        cli["Cai.Cli<br/>reference CLI"]
        web["Cai.Web<br/>Blazor SSR UI + minimal JSON API"]
    end
    nuget["NuGet package<br/>(GitHub Packages)"]
    rubrics -->|loaded by RubricCatalogStore| web
    scoring --> cli
    scoring --> web
    scoring -->|published as| nuget
    web -->|/api/rubrics, /api/score, /api/verify, /llms.txt| consumer["consumers"]
```

- **`Cai.Scoring`** — the heart: a pure, side-effect-free fold from an *evidence bundle* to a CAI
  headline and per-lens contributions. Deterministic by construction
  ([ADR-0002](adr/0002-deterministic-reproducible-scoring.md)). Published as a NuGet package.
- **`Cai.Cli`** — the reference command-line scorer; proves the fold runs anywhere.
- **`Cai.Web`** — a Blazor static-SSR site that documents the standard and a minimal HTTP API
  (`/api/rubrics`, `/api/score`, `/api/verify`) plus `/llms.txt` and a JSON-LD glossary. The public
  API is rate-limited; `/score` and `/verify` validate inbound evidence before folding.
- **`rubrics/`** — the versioned, frozen rubric catalogs cai.canine.dev owns
  ([ADR-0004](adr/0004-versioned-frozen-rubrics.md)).

## Runtime & deployment

`Cai.Web` runs as a single systemd service (`cai-web.service`) on a self-hosted host, behind nginx
that terminates TLS. Deploys are verify-before-swap with health-check rollback
([ADR-0005](adr/0005-verify-before-swap-deploy.md)); build artifacts carry an SBOM, a keyless
signature and SLSA provenance ([ADR-0006](adr/0006-supply-chain-attestation.md)).

Observability: structured logging (`ILogger`), OpenTelemetry tracing + metrics (OTLP exporter active
only when an endpoint is configured), and a `/health` readiness check the deploy probes.

## Key cross-cutting constraints

- **Determinism** — no wall-clock, randomness or ambient I/O in the scoring fold.
- **Reproducibility** — every evidence bundle names its rubric version; old versions are retained.
- **Graceful degradation** — the standard pages render even when the surveyor is unreachable; the one
  outbound call is wrapped in a resilience pipeline (timeout + retry + circuit breaker).
