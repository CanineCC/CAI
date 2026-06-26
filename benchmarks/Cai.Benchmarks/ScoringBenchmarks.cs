using BenchmarkDotNet.Attributes;
using Cai.Scoring;

namespace Cai.Benchmarks;

/// <summary>Performance guard (PF1) for the deterministic scoring fold — the library's hot path. [MemoryDiagnoser]
/// tracks allocations so a regression (extra LINQ materialisation, boxing) is visible, not silent. Run in CI via the
/// benchmarks workflow.</summary>
[MemoryDiagnoser]
public class ScoringBenchmarks
{
    private EvidenceBundle _bundle = null!;
    private string _json = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bundle = SampleBundle();
        _json = _bundle.ToJson();
    }

    /// <summary>The headline fold: dimensions → categories → lenses → headline (the work every score does).</summary>
    [Benchmark]
    public double Score() => CaiScorer.Score(_bundle).Headline;

    /// <summary>Parsing an evidence bundle off the wire — the other per-request cost.</summary>
    [Benchmark]
    public EvidenceBundle Parse() => EvidenceBundle.Parse(_json);

    private static EvidenceBundle SampleBundle() => new()
    {
        RubricVersion = "rubric-2026.08.15",
        AnalyzableProjects = 4,
        ProductionLoc = 6000,
        Dimensions =
        [
            new("D1", "CodeQuality", 9.5, 1.0),
            new("D2", "CodeQuality", 9.0, 1.0),
            new("D17", "ExplicitDebt", 9.8, 1.0),
            new("D5", "Architecture", 8.0, 1.0),
            new("D9", "Testing", 10.0, 1.0),
            new("D12", "Dependencies", 9.0, 0.9),
            new("D13", "Security", 10.0, 1.0),
            new("M1", "Docs", 6.5, 1.0),
            new("D15", "GitMining", 9.9, 1.0),
            new("D29", "SecurityCompliance", 10.0, 1.0),
        ],
        MetaDimensions =
        [
            new("P2", "productionReadiness", 10.0),
            new("S1", "securityCompliance", 10.0),
            new("M2", "maturity", 8.5),
        ],
    };
}
