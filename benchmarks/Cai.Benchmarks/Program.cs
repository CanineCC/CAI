using BenchmarkDotNet.Running;
using Cai.Benchmarks;

// Entry point: run the scoring benchmarks (or a --filter subset). In CI the benchmarks workflow runs this with a short
// job so a performance/allocation regression in the fold surfaces as a tracked artifact.
BenchmarkSwitcher.FromAssembly(typeof(ScoringBenchmarks).Assembly).Run(args);
