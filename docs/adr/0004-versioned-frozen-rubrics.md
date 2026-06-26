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
