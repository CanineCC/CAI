# 0002 — Deterministic, reproducible open scoring

- Status: Accepted
- Date: 2026-06-26

## Context

The value proposition of CAI is that a codebase health score can be *verified*, not merely trusted.
That rules out two common approaches: an LLM judge (a different answer each run, uncheckable) and a
secret proprietary score (opaque by construction). For a score to be falsifiable, anyone holding the
same evidence must be able to recompute the exact same number with no access to the analyzer that
produced the evidence.

## Decision

Scoring is a pure, deterministic fold implemented in the `Cai.Scoring` library:

- The input is an **evidence bundle** — a documented, portable JSON subset of an analyzer run
  (per-dimension 0–10 scores with confidence/coverage, language-agnostic meta-dimensions, and the
  rubric version). Producing the bundle (measuring code) is the analyzer's job; scoring it is open.
- Dimensions fold into categories (confidence-weighted), categories into lenses, and lenses into the
  0–100 headline using a **rank-weighted ordered weighted average (Yager OWA)**, worst-first, so the
  weakest areas drag hardest — never an equal-weight mean.
- The same evidence under the same rubric version always yields the same number, on any machine.

The fold is exposed three ways: the `Cai.Cli` reference CLI, the `/api/score` and `/api/verify`
HTTP endpoints, and an in-browser calculator — all calling the one library.

## Consequences

- A published headline can be independently reproduced or falsified from its evidence bundle.
- The scorer must remain free of nondeterminism (no wall-clock, randomness, or ambient I/O in the
  fold) and side-effect-free; this is a standing constraint on `Cai.Scoring`.
- Because the weights ship in the evidence, the method is fully open even though the *measurement*
  (the analyzer) and the *advisory survey* remain the surveyor's product — see [0003](0003-free-paid-firewall.md).
- Any change to the fold that could move a score for unchanged evidence is a breaking change to the
  standard and must mint a new rubric version — see [0004](0004-versioned-frozen-rubrics.md).
