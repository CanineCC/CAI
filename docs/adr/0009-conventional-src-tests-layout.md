# 0009 — Conventional `src/` and `tests/` repository layout

- Status: Accepted
- Date: 2026-06-27

## Context

The repository began with code split by deployment artifact: `app/` held the web project and `scorer/`
held the library, the CLI, the tests and a sample evidence file. That grouping answered "what ships
where" but not "what *is* this" — production code, test code and the sample data were interleaved under
`scorer/`, and there was no single place a newcomer (or a static-analysis tool) could point to and say
"this is the code under test." As the tree grew to five projects plus benchmarks, the deployment-shaped
layout stopped reflecting the roles the parts actually play.

## Decision

Group the tree by **role**, following the convention shared across the .NET and wider ecosystem:

- `src/` — all production code: `Cai.Scoring` (library), `Cai.Cli` (reference CLI), `Cai.Web` (site/API).
- `tests/` — `Cai.Tests`, the xUnit suite over the scorer.
- `benchmarks/` — `Cai.Benchmarks`, the BenchmarkDotNet hot-path benchmarks (kept separate from tests:
  benchmarks measure, they do not assert).
- `examples/` — the sample evidence bundle, no longer buried under the scorer.

The solution file (`Cai.slnx`, [ADR-0007](0007-repository-solution-file.md)) and the CI/deploy
workflows reference the new paths; the deploy itself is unchanged because it operates on the publish
*output*, not the source tree.

## Consequences

- The layout now states intent: production code, tests and benchmarks are separable at a glance, which
  is the property tooling and reviewers key off.
- All build/test/publish paths in the six workflows moved in lockstep (`src/Cai.Web`, `tests/Cai.Tests`,
  `src/Cai.Scoring`); a stale path would surface as a CI failure, which the verify-before-swap deploy
  ([ADR-0005](0005-verify-before-swap-deploy.md)) turns into a safe no-op rather than an outage.
- `ProjectReference` and doc links were updated to the new relative paths; history was preserved with
  `git mv`.
- The convention is now load-bearing: new production projects go under `src/`, new test projects under
  `tests/`.
