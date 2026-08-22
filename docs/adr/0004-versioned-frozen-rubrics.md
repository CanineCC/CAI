# 0004 — Versioned, frozen rubric catalogs

- Status: Accepted
- Date: 2026-06-26

## Context

A reproducible score is only meaningful relative to the exact criteria it was computed under. If the
rubric (the dimensions, their weights, the band cutlines) could change silently, a previously
published headline would no longer reproduce — quietly breaking the core promise of the standard.

## Decision

Rubric catalogs are **frozen and versioned**. Each version is an archived directory under
`rubrics/` (e.g. `rubric-2026.08.15/rubric-catalog.json`), served and owned by cai.canine.dev. Any
change that *could move a score for unchanged evidence* mints a new version; older versions are
retained forever. An evidence bundle always names the rubric version it was produced under, and the
scorer resolves against that exact version.

## Consequences

- Any historical CAI score can be reproduced to the exact criteria that produced it.
- The catalog set grows monotonically; old versions are never edited or deleted.
- A purely cosmetic change (wording, presentation) need not mint a version; a change to dimensions,
  weights, or cutlines must. Judging "could this move a score?" is a release-time responsibility.
- The `RubricCatalogStore` resolves `latest` to the newest version and serves any published version
  by name through the `/api/rubrics` endpoints.
- **A catalog must pin every input that can move a score, including the dimension→category map.** A
  dimension's category is not decoration: dimensions in one category average together before their
  lens's worst-first fold sees them, so re-homing a dimension changes the number for unchanged
  evidence. That assignment used to live only in the producer's code (collapsed into each catalog
  entry's `lens`, which several categories share), which meant a re-homing could move published
  scores without minting a version — the exact case this ADR says cannot happen. From
  `rubric-2026.08.18` every scored dimension carries its `category`, and `CaiScorer.Score(bundle,
  catalog)` folds under the CATALOG's assignment: evidence that contradicts the frozen map is
  refused rather than scored under a map nobody can fetch. Catalogs published before `.18` carry no
  category and keep verifying on the bundle's own, exactly as they were computed.
- **The catalog also pins the FOLD's own constants, and the band cutlines with them.** The rule above —
  pin every input that can move a score — was false as written for as long as the OWA decays, the
  critical gate, the architecture surface floor and the band cutlines lived only as `const` in
  `Cai.Scoring`. `rubricVersion` selected the dimension→category map and nothing else, so verifying a
  `rubric-2026.06.0` report ran the current build's constants, and a future change to any of them
  would have moved published numbers with no version to distinguish them. Two documents also
  disagreed: `Band.cs` said thresholds are fixed and must not vary by rubric version, while
  `QualityBarBands` already shifted all four cutlines by the evidence-carried `qualityBar` and called
  itself "the single source of truth for the cutlines." A catalog now carries a `scoring` block
  (`ScoringParameters`) holding all of it, and `CaiScorer` folds under the catalog's values.
  **Cutlines are rubric data.** They decide the published WORD, they already vary per repository by
  quality bar, and a constant frozen in code is not a stable vocabulary — it is an unenforced promise
  of one, since nothing stops it being edited and the archive cannot detect that it was. Pinning them
  in the versioned, digest-bound catalog is what makes stability *checkable*: while the values do not
  change, every catalog carries the same ones and any holder of an older report can prove it.
  Catalogs published without the block resolve to `ScoringParameters.Default` — exactly the values the
  scorer has always used — so every already-published version keeps verifying to the same number, and
  the block is omitted from the serialized form so no archived catalog's content digest changes.
