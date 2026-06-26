# Cai.Scoring

The open reference scorer for the **CAI (Codebase Assurance Index)** standard — [cai.canine.dev](https://cai.canine.dev).

Producing an evidence bundle (measuring the code) is an analyzer's job; **scoring** that bundle is this library's, and it is the single, reproducible authority: the same evidence under the same rubric yields the same number, on anyone's machine.

## What it does

`CaiScorer.Score(EvidenceBundle)` folds an evidence bundle into the 0–100 headline and ten lens scores, worst-first throughout:

1. **Categories** — each deterministic dimension's `effective = score × coverage` rolls into its category as a confidence-weighted mean (advisory/LLM dimensions excluded).
2. **Lenses** — each lens is the worst-first OWA (`q = 0.75`) of its category scores plus its meta-dimensions; the Architecture lens is floored by analyzable surface; a sub-4.0/10 contributor caps the lens band at Fair (never its number).
3. **Headline** — the sharper worst-first OWA (`q = 0.55`) of the measured lenses, with quality-bar band cutlines and a coherence cap so the headline never out-promises the weakest category.

```csharp
using Cai.Scoring;

var bundle = EvidenceBundle.Parse(json);   // or build it in code
CaiScore score = CaiScorer.Score(bundle);
Console.WriteLine($"{score.Headline:0.0} ({score.Band.Label()})");
```

Deterministic, dependency-free, and auditable — the headline reconstructs from the lens contributions. See the spec at [cai.canine.dev/spec](https://cai.canine.dev/spec).

MIT-licensed.
