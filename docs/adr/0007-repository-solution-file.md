# 0007 — A repository solution file for whole-graph analysis

- Status: Accepted
- Date: 2026-06-26

## Context

The repository is a multi-project .NET tree: `Cai.Scoring` (the library), `Cai.Cli` (the reference
CLI), `Cai.Web` (the site/API), and `Cai.Tests`. The deploy and CI steps build and test the
individual projects directly, so for a long time there was no solution file. However, Roslyn-based
analysis tools (and IDEs) load a project *graph* through an MSBuild solution: given multiple loose
`.csproj` files and no solution, they cannot determine the authoritative project set and fall back
to analyzing nothing — every compiler-dependent signal then runs on an empty workspace.

## Decision

Commit a repository solution file, `Cai.slnx` (the modern XML solution format), referencing all four
projects, grouped by their top-level folder. It is the single entry point for whole-graph tooling;
per-project `dotnet build`/`dotnet test` in CI continue to work unchanged.

## Consequences

- Roslyn-based analyzers and IDEs load the full project graph instead of returning zero projects, so
  semantic analysis (architecture, tests, sizing) reflects the real codebase.
- `dotnet build Cai.slnx` builds the whole tree in one command — convenient locally and in CI.
- The solution must be kept in sync when projects are added or removed (`dotnet sln Cai.slnx add/remove`).
- `.slnx` requires a recent SDK; the repo already targets `net10.0`, so this is not an added
  constraint.
