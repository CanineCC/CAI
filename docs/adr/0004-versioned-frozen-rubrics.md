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

## Amendment — 2026-09-16: when the rule became TRUE of the artifacts

The Consequences above were written when the capability shipped (`1d557c3`, 2026-08-22) and read as
though they described the system. They did not, and the gap is worth recording, because it is the same
gap this ADR exists to close: **a rule the producer cannot follow is not pinned, it is aspired to.**

What was true on 2026-08-22: a catalog *could* carry a `scoring` block, and `CaiScorer.Score(bundle,
catalog)` *would* fold under one.

What was NOT true, and stayed untrue for three weeks:

- **No catalog carried a block — 0 of 38 published versions.** Nor could one: the publisher is the
  kennel engine, and it emitted a hand-rolled three-field record (`rubricVersion`, `lenses`,
  `dimensions`) of its own rather than `Cai.Scoring.RubricCatalog`. The field did not exist to emit.
- **The producer could not honour a block if one existed.** The engine pinned `Cai.Scoring 0.1.3-ws-e`,
  an assembly with no `ScoringParameters` type at all — and no `Score(bundle, catalog)` overload either.
- **So the dimension→category map was not pinned in practice either**, though this ADR states it is
  "from `rubric-2026.08.18`". That is true of `Cai.Web`, which passes a catalog. The engine could not:
  it folded on the categories it had produced itself. Self-consistent, therefore never wrong — and
  unverifiable against the frozen map, which is precisely the hole the paragraph above claims to close.
- **Four of the five scoring call sites in the system passed no catalog**, including `DeliveryBuilder`
  (the mint-time trust gate) and `DeliveryVerifier` (the consumer's reproducibility check).

What is true from 2026-09-16 (`Cai.Scoring` 0.2.0):

- There is no way to fold a score, or read a band word, without naming the rubric it came from:
  `Score(EvidenceBundle)`, `Verify(EvidenceBundle, double)` and `Bands.For(double)` are DELETED and the
  catalog is non-nullable on what remains.
- The engine folds under the catalog and EMITS `Cai.Scoring.RubricCatalog` from the same declared source,
  so the published document and the number cannot describe different parameters.
- The band cutlines are read off the rubric rather than from four hand-written copies of 90/70/50/25.
- `ResolvedRubric` binds a catalog to the digest of the document it came from, and `DeliveryBuilder`
  derives the payload's `rubricContentHash` from the rubric it folded under instead of accepting one.

What is STILL not true, stated so the next reader is not misled the same way:

- ~~No published rubric version pins its own constants yet.~~ **Closed 2026-09-17**: `rubric-2026.09.13` is
  the first version to carry a `scoring` block. Its values equal `ScoringParameters.Default`, so no published
  number and no published word moved — what changed is that the criteria are now readable off the archive
  rather than inferred from whichever scorer build is running. From that version onward the rule is enforced
  forward by `TheArchiveActuallyPinsWhatTheAdrClaimsTests`.
- **The kennel product resolves per repository for a REPOSITORY's own band** (closed 2026-09-17: the band is
  decided once at ingestion from the cutlines of the rubric that run was measured under, and every per-repo
  surface displays that stored word). What remains is AGGREGATE surfaces — a corpus median, an embed scale —
  which are computed across repositories that may pin different versions and therefore have no single set of
  cutlines to be read through. They band under the defaults, which is correct exactly while every published
  version shares them. So a published block may still not carry values that DIFFER from
  `ScoringParameters.Default`, and that constraint is enforced by a test, not by this sentence.
