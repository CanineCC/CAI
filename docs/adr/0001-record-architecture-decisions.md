# 0001 — Record architecture decisions

- Status: Accepted
- Date: 2026-06-26

## Context

CAI is a small but carefully designed system: an open, reproducible scoring standard with a
deliberate boundary between what is free (the deterministic measurement) and what is paid (a
surveyor's judgment). Several of its choices are non-obvious and easy to erode by a well-meaning
change — determinism, the rubric-versioning discipline, the data-ownership split with the surveyor.
Decisions like these were previously only visible in commit messages and code comments, which are
hard to discover and easy to lose.

## Decision

Record significant architecture decisions as Architecture Decision Records (ADRs) under
`docs/adr/`, one Markdown file per decision, numbered sequentially. Each ADR states its context,
the decision, and the consequences. ADRs are immutable once accepted; a later decision that changes
course is a new ADR that supersedes the earlier one.

## Consequences

- New maintainers (human or agent) can read *why* the system is shaped as it is, not reverse-engineer
  it from code.
- There is a small, ongoing cost: a genuinely architectural change should come with an ADR.
- The decision log is versioned with the code, so it travels with any clone and is reviewable in PRs.
- Trivial or easily-reversible choices stay out of the log — ADRs are for decisions with lasting,
  cross-cutting consequences.
