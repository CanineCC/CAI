# Cai.Scoring

The open reference scorer for the **CAI (Code Assurance Index)** standard — [codeassuranceindex.info](https://codeassuranceindex.info).

Producing an evidence bundle (measuring the code) is an analyzer's job; **scoring** that bundle is this library's, and it is the single, reproducible authority: the same evidence under the same rubric yields the same number, on anyone's machine.

## What it does

`CaiScorer.Score(EvidenceBundle)` folds an evidence bundle into the 0–100 headline and ten lens scores, worst-first throughout:

1. **Categories** — each deterministic dimension's `effective = score × coverage` rolls into its category as a confidence-weighted mean (advisory/LLM dimensions excluded).
2. **Lenses** — each lens is the worst-first OWA (`q = 0.75`) of its category scores plus its meta-dimensions; the Architecture lens is floored by analyzable surface; a sub-4.0/10 contributor caps the lens band at Fair (never its number).
3. **Headline** — the sharper worst-first OWA (`q = 0.55`) of the measured lenses, with quality-bar band cutlines and a coherence cap so the headline never out-promises the weakest category.

```csharp
using Cai.Scoring;

var bundle  = EvidenceBundle.Parse(bundleJson);    // or build it in code
var catalog = RubricCatalog.Parse(catalogJson);    // the rubric version the bundle names

CaiScore score = CaiScorer.Score(bundle, catalog);
Console.WriteLine($"{score.Headline:0.0} ({score.Band.Label()})");
```

The catalog is **required**, not a convenience: it pins the fold's constants and the band cutlines, so a number is only meaningful beside the rubric version it was computed under. There is deliberately no overload that folds without one.

## Serializing

`Parse(string json)` and `ToJson()` live on the types that are **documents on the wire** — here `EvidenceBundle` and `RubricCatalog`, and in `Cai.Delivery` the package and the key files. Their parts (`CatalogDimension`, `DimensionScore`, `LensInput`, …) deliberately carry neither: a dimension on its own is not something anyone publishes, and giving each part its own entry point invites round-tripping a fragment whose meaning depends on the document around it. Serialize a part by serializing the document that contains it.

Deterministic, dependency-free, and auditable — the headline reconstructs from the lens contributions. See the spec at [codeassuranceindex.info/spec](https://codeassuranceindex.info/spec).

Apache-2.0, matching the [repository licence](https://github.com/CanineCC/CAI/blob/main/LICENSE).
