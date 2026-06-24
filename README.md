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

Bands: **Exemplary** 90–100 · **Healthy** 70–89 · **Fair** 50–69 · **Poor** 25–49 · **Critical** 0–24.

## The reference scorer (`/scorer`)

The open, reproducible half of the standard, in C# (Apache-2.0). Measuring a codebase into an **evidence bundle** is
the analyzer's job; **scoring** the bundle is open — the headline is a worst-first ordered-weighted average of the lens
scores (`Σ lensScore × owaWeight`), banded. Because the weights are published in the evidence, anyone can reproduce or
falsify a published number with no access to the engine.

```
cd scorer
dotnet test                                                     # the scorer's own tests
dotnet run --project Cai.Cli -- score  examples/evidence.sample.json
dotnet run --project Cai.Cli -- verify examples/evidence.sample.json             # ✓ reproduced
dotnet run --project Cai.Cli -- verify examples/evidence.sample.json --expect 90 # ✗ mismatch (exit 1)
```

`Cai.Scoring` is the library (bands, lenses, evidence bundle, the fold); `Cai.Cli` is the `cai` tool; `Cai.Tests`
covers determinism, banding, the fold, and verify. The evidence-bundle format + the algorithm are documented at
[cai.canine.dev/spec](https://cai.canine.dev/spec.html#evidence).

## The app (`/app/Cai.Web`)

cai.canine.dev is a hosted ASP.NET Core + Blazor app — it OWNS the rubric catalogs and serves them, the scorer, and the
content over one origin:

| Route | What it is |
|---|---|
| `/` | The definition + the Open · Verifiable · Referenced trinity + the bands. |
| `/spec` | The open algorithm (dimensions → lenses → CAI), versioned with the rubric-version picker. |
| `/lenses` | The ten lenses — generated live from the catalog. |
| `/dimensions` | The full ~124-dimension catalog, by lens + rubric version. |
| `/calculator` | Paste an evidence bundle → CAI + lens roll-up (and verify the claim if present). |
| `/verify` | The proof engine — reproduce a published number from its evidence. |
| `/registry` | The public record of signed surveys. |
| `/badge` | The badge + the honest mark-usage policy. |
| `/api-reference` | The rubric + scoring API (what watchdog calls). |
| `/api/rubrics`, `/api/rubrics/{v}/catalog`, `/api/score`, `/api/verify` | The JSON API (rate-limited; loopback exempt). |

Run locally: `cd app/Cai.Web && Rubrics__Root=$(pwd)/../../rubrics dotnet run` → http://localhost:5000. It's deployed
as a systemd service on canine-wrx1 with nginx/SSL on canine-dgx1 — see [`deploy/DEPLOY.md`](deploy/DEPLOY.md).

## License

The standard and this content are Apache-2.0 (see `LICENSE`).
