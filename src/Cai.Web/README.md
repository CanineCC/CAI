# Cai.Web

The cai.canine.dev site: the public face of the CAI standard. A Blazor static-SSR app that documents
the standard and exposes the rubric + scoring **JSON API**.

## What it serves

- **UI** — the standard, the spec, the lenses, the dimension catalog, an in-browser calculator, the
  verify page, and the public registry view.
- **API** (`/api`, rate-limited):
  - `GET /api/rubrics` — published rubric versions, newest first.
  - `GET /api/rubrics/{version}/catalog` — a version's full dimension × lens catalog (`latest` resolves).
  - `POST /api/score` — fold an evidence bundle to a CAI headline + per-lens contributions.
  - `POST /api/verify` — check a published headline reproduces from its evidence.
- **Referenceable** — `/llms.txt` and `/glossary.jsonld` (schema.org `DefinedTermSet`).
- **Ops** — `GET /health` readiness probe.

## How it's wired

- Scoring is delegated to [`Cai.Scoring`](../Cai.Scoring) — the one deterministic authority.
- Rubric catalogs are loaded from `rubrics/` via `RubricCatalogStore` (configurable with `Rubrics:Root`).
- Public read API is gated by a chained rate limiter (per-IP, 1/s · 3/min · 15/day), not auth; the
  surveyor's aggregate stats are fetched server-side through a resilient `HttpClient`.
- Observability: structured `ILogger`, OpenTelemetry tracing + metrics (OTLP exporter active only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set), and the `/health` check.
- Security: default-deny authorization with explicit public opt-out, security response headers (CSP,
  X-Frame-Options, …), secure antiforgery cookie, and validated inbound payloads.

## Run locally

```bash
dotnet run --project src/Cai.Web
```

Deployed to cai.canine.dev by [`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml);
see [docs/architecture.md](../../docs/architecture.md) and the [ADRs](../../docs/adr/README.md).
