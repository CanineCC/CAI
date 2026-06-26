# 0003 — The free/paid firewall: open standard vs surveyor judgment

- Status: Accepted
- Date: 2026-06-26

## Context

CAI is both an open standard and the basis of a commercial service (the independent surveyor,
watchdog.canine.dev). Without a crisp boundary, the two blur: either the "open" standard quietly
depends on proprietary pieces, or the paid service has nothing defensible to sell. We need a line
that keeps the standard genuinely free and reproducible while leaving room for a paid product.

## Decision

Draw an explicit firewall:

- **Free / open:** the deterministic measurement — the rubric, the dimensions, the scoring
  algorithm, and the findings that fall directly out of the evidence. This is what lives in this
  repository (Apache-2.0 scorer; CC-BY versioned spec) and what anyone can reproduce.
- **Paid / surveyor:** independent, signed surveys — the advisory deductions, the prioritised "what
  to do about it", and non-score enhancements that require judgment. These are the surveyor's
  product and are not part of the open fold.

Data ownership follows the same line: cai.canine.dev owns the rubric catalogs (the standard); the
surveyor owns the survey records. The site only ever fetches the surveyor's *public aggregate*
scale (LoC scanned, completed scans) for display, server-side and best-effort.

## Consequences

- The standard cannot be captured: a score is always reproducible from open inputs, independent of
  any one surveyor.
- The mark is protected even though the method is free — only spec-reproducible results may carry the
  "CAI" mark.
- The site must degrade gracefully when the surveyor is unreachable (the standard pages render
  without the aggregate stats); this drives the resilience around that one outbound call.
- New features must be classified on one side of the firewall or the other; "measurement" features
  belong here, "judgment" features belong to the surveyor.
