# CAI — the Codebase Assurance Index

**An open, reproducible 0–100 standard for the health of a C#/.NET codebase. Same code in, same score out.**

This repository is the home of the CAI standard: the website (cai.canine.dev) and — over time — the reference
implementation that lets anyone compute and verify the number.

The model is **method open, judgment sold**: the *standard* (how a codebase is measured) is open and free; the
independent, signed *survey* (the deductions and what to do) is a service from the surveyor,
[watchdog.canine.dev](https://watchdog.canine.dev).

## The standard

- One **0–100** score for a whole codebase, computed deterministically from evidence under a **frozen, versioned rubric**.
- Composed of ~120 dimensions grouped into **ten lenses** (five core, always on; the rest model-aware).
- **Reproducible**: same evidence + same rubric version ⇒ the same number. Falsifiable by design.
- A **measurement, never a certification** — the tool evidences the automatable slice; a named human attests the rest.

Bands: **Strong** 80–100 · **Fair** 60–79 · **Weak** 40–59 · **Poor** 0–39.

## The site (`/site`)

The cai.canine.dev standards site — a static, framework-free site:

| Page | What it is |
|---|---|
| `index.html` | The definition + the Open · Verifiable · Referenced trinity + the firewall + the funnel. |
| `spec.html` | The open algorithm, versioned. |
| `lenses.html` | The ten lenses — what each measures, why, what it protects against. |
| `cli.html` | Compute it yourself — the reference implementation. |
| `verify.html` | The proof engine — reproduce any CAI from its evidence; check the signature. |
| `registry.html` | The public record of signed surveys. |
| `badge.html` | The badge + the honest mark-usage policy. |
| `llms.txt`, `cai.jsonld` | Machine-readable: the standard for search/LLMs (schema.org `DefinedTermSet`). |

It deploys to **cai.canine.dev** via GitHub Pages (`.github/workflows/pages.yml`, custom domain in `site/CNAME`). To
preview locally: `cd site && python3 -m http.server` → http://localhost:8000.

## License

The standard and this content are Apache-2.0 (see `LICENSE`).
