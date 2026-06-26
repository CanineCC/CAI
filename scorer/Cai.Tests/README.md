# Cai.Tests

xUnit tests for the CAI reference scorer ([`Cai.Scoring`](../Cai.Scoring)). They pin the behaviour that
makes the standard trustworthy: that the fold is **deterministic and reproducible**, and that its rules
(confidence weighting, coverage, the worst-first OWA, the architecture surface floor, quality-bar bands,
band coherence, critical gating) behave exactly as specified.

The suite reaches the library's internal helpers via `InternalsVisibleTo` — those types are
implementation details of the fold, not public contract (see
[ADR-0007](../../docs/adr/0007-repository-solution-file.md) and the API-surface decision).

## Run

```bash
dotnet test scorer/Cai.Tests       # or: dotnet test Cai.slnx
```

The deploy pipeline runs these on every change; a failure is a safe no-op (the live site keeps serving).
