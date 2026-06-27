# Cai.Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks for the CAI scoring hot paths in
[`Cai.Scoring`](../../src/Cai.Scoring): the deterministic fold (`CaiScorer.Score`) and evidence-bundle
parsing. `[MemoryDiagnoser]` tracks allocations so a regression (extra LINQ materialisation, boxing)
shows up rather than slipping by.

## Run

```bash
# All benchmarks
dotnet run -c Release --project benchmarks/Cai.Benchmarks

# A quick pass (Short job) — what CI runs
dotnet run -c Release --project benchmarks/Cai.Benchmarks -- --filter '*' --job short
```

CI runs the Short job via [`.github/workflows/benchmarks.yml`](../../.github/workflows/benchmarks.yml).
See [ADR-0002](../../docs/adr/0002-deterministic-reproducible-scoring.md) for why the fold must stay fast
and allocation-light.
