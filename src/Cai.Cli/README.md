# Cai.Cli

The reference command-line scorer (`cai`): proves the CAI fold runs anywhere, with no service and no
network. Give it an evidence bundle, get the deterministic headline back — the same number the API and
the in-browser calculator produce, because all three call the one [`Cai.Scoring`](../Cai.Scoring) library.

## Usage

```bash
# Score an evidence bundle to its CAI headline + per-lens contributions
dotnet run --project src/Cai.Cli -- path/to/evidence.json
```

The input is a CAI **evidence bundle** (see [`examples/evidence.sample.json`](../../examples/evidence.sample.json)) —
the portable, documented record a score is computed from. The output is the reproducible CAI score:
same evidence under the same rubric version always folds to the same number.

## Why it exists

It is the executable proof that the standard is open: anyone can recompute — or falsify — a published
CAI from its evidence with nothing but this CLI. See [docs/adr/0002](../../docs/adr/0002-deterministic-reproducible-scoring.md).
