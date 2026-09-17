using System.Globalization;
using System.Reflection;
using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// ★★ EVERY PARAMETER THE CATALOG PINS MUST DEMONSTRABLY CHANGE AN OUTCOME — and every parameter must have a probe.
///
/// <para>This is the guard against the defect class the 2026-09 rubric-fold work was written to remove: a
/// score-moving input that exists in the model and that nothing actually reads. `ScoringParameters` shipped on
/// 2026-08-22 and no producer emitted it and no scorer in the product folded under it for three weeks, and nothing
/// failed, because every test on either side asserted its own half.</para>
///
/// <para>Two halves, and the second is the one with a future:</para>
/// <list type="number">
///   <item>each probe PERTURBS one leaf parameter and asserts the fold's observable output changes — a parameter
///     nothing reads cannot pass;</item>
///   <item>a reflective sweep asserts every leaf of <see cref="ScoringParameters"/> HAS a probe — so a parameter
///     added tomorrow fails this test until someone shows it matters.</item>
/// </list>
///
/// <para>Without (2) this is a list that decays; with it, the list cannot fall behind the type.</para>
/// </summary>
public sealed class EveryScoreMovingParameterMovesAScoreTests
{
    /// <summary>
    /// A bundle that spans the WHOLE band range and every lens group — 94, 88, 77, 60, 42, 15 on the 0–100 scale.
    ///
    /// <para>The range is the point. A probe for <c>Bands.Poor</c> cannot observe anything unless some lens sits near
    /// the Poor line, and the cutlines are evaluated top-down, so a bundle bunched in the 60s proves nothing about
    /// the lower two. The first draft of this fixture was bunched, and five probes correctly reported that they
    /// reached nothing.</para>
    ///
    /// <para>Lens groups covered: foundational (codeHealth, architecture), operational (maturity,
    /// productionReadiness), safety (securityCompliance), and default — which needs a lens OUTSIDE those five, so it
    /// comes from a meta-dimension.</para>
    /// </summary>
    private static EvidenceBundle Spread(string? bar = null) => new()
    {
        RubricVersion = "rubric-test",
        QualityBar = bar,
        AnalyzableProjects = 6,
        ProductionLoc = 9000,
        Dimensions =
        [
            new DimensionScore("D1", "code-quality", 9.4, 1.0),          // -> codeHealth        foundational
            new DimensionScore("D2", "explicit-debt", 3.0, 1.0),          // -> codeHealth TOO — two items in one
                                                                          //    lens, which is what WithinLensQ acts on
            new DimensionScore("D5", "architecture", 7.7, 1.0),          // -> architecture 77  foundational
            new DimensionScore("D40", "docs", 4.2, 1.0),                 // -> maturity     42  operational
            new DimensionScore("D9", "testing", 1.5, 1.0),               // -> readiness    15  operational
            new DimensionScore("D30", "security-compliance", 8.8, 1.0),  // -> security     88  safety
        ],
        MetaDimensions =
        [
            new MetaDimensionScore("AX1", "accessibility", 6.0),         // -> accessibility 60  default group
        ],
    };

    /// <summary>Carries a measured contributor below the critical gate, so the gate's band cap is observable.</summary>
    private static EvidenceBundle WithCriticalContributor() => new()
    {
        RubricVersion = "rubric-test",
        AnalyzableProjects = 6,
        ProductionLoc = 9000,
        Dimensions =
        [
            new DimensionScore("D1", "code-quality", 9.0, 1.0),
            new DimensionScore("D2", "code-quality", 4.5, 1.0), // between the default gate (4.0) and a raised one
            new DimensionScore("D30", "security", 8.0, 1.0),
        ],
    };

    /// <summary>Too little analysable surface to grade cross-project architecture — the floor's precondition.</summary>
    private static EvidenceBundle LowSurface() => new()
    {
        RubricVersion = "rubric-test",
        AnalyzableProjects = 1,
        ProductionLoc = 400,
        Dimensions =
        [
            new DimensionScore("D5", "architecture", 10.0, 1.0),
            new DimensionScore("D1", "code-quality", 8.0, 1.0),
        ],
    };

    /// <summary>What a reader of the artifact actually sees: the headline, and every lens's score, word and gate.</summary>
    private static string Observe(EvidenceBundle evidence, ScoringParameters parameters)
    {
        var scored = CaiScorer.Score(
            evidence, new RubricCatalog { RubricVersion = evidence.RubricVersion, Scoring = parameters });

        var lenses = scored.Lenses.OrderBy(l => l.Lens, StringComparer.Ordinal).Select(l =>
            $"{l.Lens}={l.Score.ToString("R", CultureInfo.InvariantCulture)}/{l.Band}/{l.CriticalGated}");

        return $"{scored.Headline.ToString("R", CultureInfo.InvariantCulture)}/{scored.Band}|{string.Join(",", lenses)}";
    }

    private sealed record Probe(string Path, ScoringParameters Perturbed, EvidenceBundle Evidence);

    private static ScoringParameters Bar(Func<QualityBarParameters, QualityBarParameters> f) =>
        ScoringParameters.Default with { QualityBar = f(ScoringParameters.Default.QualityBar) };

    private static QualityBarParameters WithOffset(string tier, double value)
    {
        var offsets = ScoringParameters.Default.QualityBar.Offsets.ToDictionary(
            kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        offsets[tier] = value;
        return ScoringParameters.Default.QualityBar with { Offsets = offsets };
    }

    private static QualityBarParameters WithFactor(string group, double value)
    {
        var factors = ScoringParameters.Default.QualityBar.LensGroupFactors.ToDictionary(
            kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        factors[group] = value;
        return ScoringParameters.Default.QualityBar with { LensGroupFactors = factors };
    }

    /// <summary>One probe per leaf parameter. The clamps are probed under an offset large enough to reach them —
    /// they are safety rails and do not bind at the published offsets, which is a property of the values, not a
    /// licence to leave them unexercised.</summary>
    private static IReadOnlyList<Probe> Probes() =>
    [
        new("WithinLensQ", ScoringParameters.Default with { WithinLensQ = 0.20 }, Spread()),
        new("AcrossLensQ", ScoringParameters.Default with { AcrossLensQ = 0.05 }, Spread()),
        new("CriticalGate", ScoringParameters.Default with { CriticalGate = 6.0 }, WithCriticalContributor()),

        new("ArchitectureSurface.MinProjects",
            ScoringParameters.Default with
            {
                ArchitectureSurface = ScoringParameters.Default.ArchitectureSurface with { MinProjects = 1 },
            },
            LowSurface()),
        new("ArchitectureSurface.MinProductionLoc",
            ScoringParameters.Default with
            {
                ArchitectureSurface = ScoringParameters.Default.ArchitectureSurface with { MinProductionLoc = 100 },
            },
            LowSurface()),
        new("ArchitectureSurface.LowSurfaceCap",
            ScoringParameters.Default with
            {
                ArchitectureSurface = ScoringParameters.Default.ArchitectureSurface with { LowSurfaceCap = 20.0 },
            },
            LowSurface()),

        new("Bands.Exemplary", ScoringParameters.Default with
            { Bands = ScoringParameters.Default.Bands with { Exemplary = 60.0 } }, Spread()),
        new("Bands.Healthy", ScoringParameters.Default with
            { Bands = ScoringParameters.Default.Bands with { Healthy = 40.0 } }, Spread()),
        new("Bands.Fair", ScoringParameters.Default with
            { Bands = ScoringParameters.Default.Bands with { Fair = 30.0 } }, Spread()),
        new("Bands.Poor", ScoringParameters.Default with
            { Bands = ScoringParameters.Default.Bands with { Poor = 95.0 } }, Spread()),

        new("QualityBar.Offsets[prototype]", Bar(_ => WithOffset(QualityBarTiers.Prototype, -60.0)), Spread("prototype")),
        new("QualityBar.Offsets[preview]", Bar(_ => WithOffset(QualityBarTiers.Preview, -60.0)), Spread("preview")),
        new("QualityBar.Offsets[production]", Bar(_ => WithOffset(QualityBarTiers.Production, -60.0)), Spread("production")),
        new("QualityBar.Offsets[mission-critical]", Bar(_ => WithOffset(QualityBarTiers.MissionCritical, 60.0)), Spread("mission-critical")),

        new("QualityBar.LensGroupFactors[foundational]", Bar(_ => WithFactor("foundational", 8.0)), Spread("prototype")),
        new("QualityBar.LensGroupFactors[operational]", Bar(_ => WithFactor("operational", 8.0)), Spread("prototype")),
        new("QualityBar.LensGroupFactors[safety]", Bar(_ => WithFactor("safety", 0.0)), Spread("prototype")),
        new("QualityBar.LensGroupFactors[default]", Bar(_ => WithFactor("default", 8.0)), Spread("prototype")),

        // The clamps. They do NOT bind at the published offsets — that is a property of the values, not a licence to
        // leave them unexercised — so each is moved INTO range and the band it then pins is what differs.
        new("QualityBar.ExemplaryCeiling",
            Bar(q => q with { ExemplaryCeiling = 60.0 }), Spread("mission-critical")),
        new("QualityBar.PoorFloor",
            Bar(q => q with { PoorFloor = 20.0 }), Spread("prototype")),
    ];

    [Theory]
    [MemberData(nameof(ProbeCases))]
    public void Perturbing_this_parameter_changes_what_a_reader_sees(string path)
    {
        var probe = Probes().Single(p => p.Path == path);

        var baseline = Observe(probe.Evidence, ScoringParameters.Default);
        var perturbed = Observe(probe.Evidence, probe.Perturbed);

        Assert.True(
            baseline != perturbed,
            $"Changing '{path}' changed NOTHING a reader sees ({baseline}). Either the fold does not read it — the "
            + "defect this whole guard exists for — or the probe does not reach it. Both are faults; neither is a "
            + "reason to delete the probe.");
    }

    public static TheoryData<string> ProbeCases()
    {
        var data = new TheoryData<string>();
        foreach (var probe in Probes())
        {
            data.Add(probe.Path);
        }

        return data;
    }

    [Fact]
    public void Every_leaf_of_ScoringParameters_has_a_probe()
    {
        // ★ The half with a future. A parameter added tomorrow fails here until someone demonstrates it matters —
        // which is exactly the question nobody asked of the scoring block in 2026-08.
        var declared = LeafPaths(ScoringParameters.Default, "").OrderBy(p => p, StringComparer.Ordinal).ToList();
        var probed = Probes().Select(p => p.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.Equal(declared, probed);
    }

    /// <summary>Every score-moving leaf of a parameter tree: doubles and ints directly, dictionaries expanded per key,
    /// nested parameter records recursed into.</summary>
    private static IEnumerable<string> LeafPaths(object node, string prefix)
    {
        foreach (var property in node.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var value = property.GetValue(node);
            switch (value)
            {
                case IReadOnlyDictionary<string, double> table:
                    foreach (var key in table.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        yield return $"{prefix}{property.Name}[{key}]";
                    }

                    break;

                case double or int:
                    yield return $"{prefix}{property.Name}";
                    break;

                case not null when value.GetType().Namespace == typeof(ScoringParameters).Namespace:
                    foreach (var nested in LeafPaths(value, $"{prefix}{property.Name}."))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }
}
