using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The reference-scorer contract: dimensions → categories (confidence-weighted) → lenses (worst-first OWA q=0.75, plus
/// the architecture surface floor) → headline (worst-first OWA q=0.55), with quality-bar lens bands, critical-gating,
/// and the worst-category coherence cap. Every expectation is hand-computed from the rubric constants; the golden
/// vector reproduces a real published Watchdog headline.
/// </summary>
public sealed class CaiScorerTests
{
    private static DimensionScore Dim(string id, DimensionCategory category, double score,
        double conf = 1.0, double coverage = 1.0, bool advisory = false) =>
        new(id, category.ToString(), score, conf) { Coverage = coverage, Advisory = advisory };

    private static MetaDimensionScore Meta(string id, string lens, double? score, bool advisory = false) =>
        new(id, lens, score) { Advisory = advisory };

    // A surface large enough that the architecture floor never bites unless a test sets it small.
    private static EvidenceBundle Bundle(params DimensionScore[] dims) =>
        new() { RubricVersion = "rubric-test", AnalyzableProjects = 5, ProductionLoc = 5000, Dimensions = dims };

    private static LensResult Lens(CaiScore s, string lens) => s.Lenses.Single(l => l.Lens == lens);

    // ── Stage 1: the category layer (the piece the old direct dim→lens fold was missing) ──────────────────────────

    [Fact]
    public void Dimensions_in_a_category_average_before_the_lens_sees_them()
    {
        // [8,6] in ONE category → plain mean 70, NOT a worst-first dim OWA (which would be 68.57). The category layer
        // is exactly what stops one weak dimension from dragging as if it were a whole lens.
        var ch = Lens(CaiScorer.Score(Bundle(
            Dim("D1", DimensionCategory.CodeQuality, 8), Dim("D2", DimensionCategory.CodeQuality, 6))), "codeHealth");
        Assert.Equal(70.0, ch.Score, 2);
    }

    [Fact]
    public void Two_categories_in_one_lens_fold_worst_first()
    {
        // codeHealth ← CodeQuality(80) + ExplicitDebt(50); OWA q=0.75 → 50·(1/1.75) + 80·(0.75/1.75) = 62.857.
        var ch = Lens(CaiScorer.Score(Bundle(
            Dim("D1", DimensionCategory.CodeQuality, 8), Dim("D17", DimensionCategory.ExplicitDebt, 5))), "codeHealth");
        Assert.Equal(62.857, ch.Score, 2);
    }

    [Fact]
    public void Category_is_confidence_weighted()
    {
        // (10·0.2 + 4·0.8) / (0.2+0.8) ·10 = 52.
        var ch = Lens(CaiScorer.Score(Bundle(
            Dim("D1", DimensionCategory.CodeQuality, 10, conf: 0.2),
            Dim("D2", DimensionCategory.CodeQuality, 4, conf: 0.8))), "codeHealth");
        Assert.Equal(52.0, ch.Score, 2);
    }

    [Fact]
    public void Coverage_scales_the_effective_score()
    {
        // effective = 10 × 0.5 = 5 → category 50.
        var ch = Lens(CaiScorer.Score(Bundle(Dim("D1", DimensionCategory.CodeQuality, 10, coverage: 0.5))), "codeHealth");
        Assert.Equal(50.0, ch.Score, 2);
    }

    [Fact]
    public void Advisory_and_zero_confidence_dimensions_are_excluded_from_the_number()
    {
        var withNoise = CaiScorer.Score(Bundle(
            Dim("real", DimensionCategory.CodeQuality, 8),
            Dim("llm", DimensionCategory.CodeQuality, 1, advisory: true),
            Dim("unmeasured", DimensionCategory.CodeQuality, 0, conf: 0))).Headline;
        var clean = CaiScorer.Score(Bundle(Dim("real", DimensionCategory.CodeQuality, 8))).Headline;
        Assert.Equal(clean, withNoise, 6);
    }

    // ── Stage 2: lens fold, surface floor, meta-dims, critical gate ───────────────────────────────────────────────

    [Fact]
    public void Architecture_lens_is_dropped_when_there_are_no_analyzable_projects()
    {
        var s = CaiScorer.Score(new EvidenceBundle
        {
            RubricVersion = "r",
            AnalyzableProjects = 0,
            ProductionLoc = 100,
            Dimensions = [Dim("AX1", DimensionCategory.Architecture, 10), Dim("D1", DimensionCategory.CodeQuality, 5)],
        });
        Assert.DoesNotContain(s.Lenses, l => l.Lens == "architecture");
        Assert.Equal(50.0, s.Headline, 2); // only codeHealth (50) remains
    }

    [Fact]
    public void Architecture_lens_is_capped_on_a_thin_single_project_surface()
    {
        var arch = Lens(CaiScorer.Score(new EvidenceBundle
        {
            RubricVersion = "r",
            AnalyzableProjects = 1,
            ProductionLoc = 100, // below both bars
            Dimensions = [Dim("AX1", DimensionCategory.Architecture, 10)],
        }), "architecture");
        Assert.Equal(ArchitectureSurfaceFloor.LowSurfaceCap, arch.Score, 2); // 100 capped to 69
    }

    [Fact]
    public void A_single_large_library_clears_the_surface_floor_on_loc_alone()
    {
        var arch = Lens(CaiScorer.Score(new EvidenceBundle
        {
            RubricVersion = "r",
            AnalyzableProjects = 1,
            ProductionLoc = 2000, // ≥ 1500 → clears the bar
            Dimensions = [Dim("AX1", DimensionCategory.Architecture, 10)],
        }), "architecture");
        Assert.Equal(100.0, arch.Score, 2);
    }

    [Fact]
    public void Meta_dimensions_feed_their_lens_directly_at_score_times_ten()
    {
        var s = CaiScorer.Score(new EvidenceBundle
        {
            RubricVersion = "r",
            AnalyzableProjects = 5,
            ProductionLoc = 5000,
            MetaDimensions = [Meta("M1", "maturity", 8)],
        });
        Assert.Equal(80.0, Lens(s, "maturity").Score, 2);
    }

    [Fact]
    public void Advisory_and_unmeasured_meta_dimensions_are_excluded()
    {
        var s = CaiScorer.Score(new EvidenceBundle
        {
            RubricVersion = "r",
            AnalyzableProjects = 5,
            ProductionLoc = 5000,
            MetaDimensions =
            [
                Meta("M1", "maturity", 8),
                Meta("M2", "maturity", 1, advisory: true), // advisory — excluded
                Meta("M3", "maturity", null),               // unmeasured — excluded
            ],
        });
        Assert.Equal(80.0, Lens(s, "maturity").Score, 2);
    }

    [Fact]
    public void Critical_gate_caps_a_lens_band_at_Fair_without_changing_the_number()
    {
        // (3.5+10+10)/3 ·10 = 78.33 → Healthy number, but the 3.5 contributor (< 4.0) gates the band to Fair.
        var ch = Lens(CaiScorer.Score(Bundle(
            Dim("x", DimensionCategory.CodeQuality, 3.5),
            Dim("y", DimensionCategory.CodeQuality, 10),
            Dim("z", DimensionCategory.CodeQuality, 10))), "codeHealth");
        Assert.True(ch.CriticalGated);
        Assert.True(ch.Score >= 70);
        Assert.Equal(Band.Fair, ch.Band);
    }

    // ── Stage 3: headline, quality bar, coherence ────────────────────────────────────────────────────────────────

    [Fact]
    public void Headline_is_the_worst_first_owa_of_the_lenses()
    {
        // codeHealth 70, architecture 90 → OWA q=0.55: 70·(1/1.55) + 90·(0.55/1.55) = 77.097.
        var s = CaiScorer.Score(Bundle(
            Dim("D1", DimensionCategory.CodeQuality, 7), Dim("AX1", DimensionCategory.Architecture, 9)));
        Assert.Equal(77.097, s.Headline, 2);
        Assert.Equal(Band.Healthy, s.Band);
    }

    [Fact]
    public void The_quality_bar_shifts_lens_bands_not_the_number()
    {
        // codeHealth 68: production → Adequate (Fair); poc → Strong (Healthy), since the Foundational healthy line
        // drops to 70 + (-18·0.4) = 62.8. The SCORE is identical either way.
        var dims = new[] { Dim("D1", DimensionCategory.CodeQuality, 6.8) };
        var prod = new EvidenceBundle { RubricVersion = "r", AnalyzableProjects = 5, ProductionLoc = 5000, Dimensions = dims };
        var poc = prod with { QualityBar = "poc" };

        Assert.Equal(68.0, Lens(CaiScorer.Score(prod), "codeHealth").Score, 2);
        Assert.Equal(68.0, Lens(CaiScorer.Score(poc), "codeHealth").Score, 2);
        Assert.Equal(Band.Fair, Lens(CaiScorer.Score(prod), "codeHealth").Band);
        Assert.Equal(Band.Healthy, Lens(CaiScorer.Score(poc), "codeHealth").Band);
    }

    [Fact]
    public void Headline_band_is_capped_so_it_never_out_promises_the_weakest_category()
    {
        // Worst category Poor (30) but headline lands Healthy → cap to one band above Poor = Fair.
        var (band, note) = BandCoherence.Cap(Band.Healthy, [30.0, 100.0]);
        Assert.Equal(Band.Fair, band);
        Assert.NotEqual("", note);

        // Worst category Fair (65): Healthy is exactly one above Fair, so no cap.
        Assert.Equal(Band.Healthy, BandCoherence.Cap(Band.Healthy, [65.0, 100.0]).Band);
    }

    // ── Determinism, weights, bands, verify, json ────────────────────────────────────────────────────────────────

    [Fact]
    public void Is_deterministic()
    {
        var b = Bundle(Dim("D1", DimensionCategory.CodeQuality, 7.3),
            Dim("AX1", DimensionCategory.Architecture, 6.1), Dim("M1", DimensionCategory.Docs, 8.4));
        Assert.Equal(CaiScorer.Score(b).Headline, CaiScorer.Score(b).Headline);
    }

    [Theory]
    [InlineData(95, Band.Exemplary)]
    [InlineData(90, Band.Exemplary)]
    [InlineData(89.9, Band.Healthy)]
    [InlineData(70, Band.Healthy)]
    [InlineData(50, Band.Fair)]
    [InlineData(25, Band.Poor)]
    [InlineData(24.9, Band.Critical)]
    [InlineData(0, Band.Critical)]
    public void Bands_at_the_canonical_thresholds(double score, Band expected) =>
        Assert.Equal(expected, Bands.For(score));

    [Fact]
    public void Owa_weights_put_q_pow_0_on_the_worst_and_sum_to_one()
    {
        var w = CaiScorer.OwaWeights([86, 58, 72], 0.55); // worst = 58 (index 1)
        Assert.Equal(1.0, w.Sum(), 9);
        Assert.True(w[1] > w[2] && w[2] > w[0]);
    }

    [Fact]
    public void Golden_vector_reproduces_a_real_published_headline()
    {
        // The production scorecard for run 019f007f-…3ad7: lenses Readiness 41.4, Maturity 44.2, Security 54.3,
        // Code Health 100 → headline 48.93 (the card's "49 CAI"). cai's across-lens fold must reproduce it exactly.
        var bundle = new EvidenceBundle
        {
            RubricVersion = "rubric-2026.08.15",
            HeadlineScore = 48.93,
            Lenses =
            [
                new("productionReadiness", 41.4, 0), new("maturity", 44.2, 0),
                new("securityCompliance", 54.3, 0), new("codeHealth", 100, 0),
            ],
        };
        var s = CaiScorer.Score(bundle);
        Assert.Equal(48.93, s.Headline, 1);
        Assert.True(CaiScorer.Verify(bundle).Reproduced);
    }

    [Fact]
    public void Lens_only_bundle_with_shipped_weights_reproduces_exactly()
    {
        var bundle = new EvidenceBundle
        {
            RubricVersion = "r",
            HeadlineScore = 72.2,
            Lenses =
            [
                new("securityCompliance", 58, 0.24), new("maturity", 65, 0.20), new("architecture", 72, 0.18),
                new("codeHealth", 86, 0.14), new("productionReadiness", 80, 0.12),
                new("accessibility", 90, 0.06), new("performance", 88, 0.06),
            ],
        };
        Assert.True(CaiScorer.Verify(bundle).Reproduced);
    }

    [Fact]
    public void Verify_rejects_a_headline_that_does_not_follow_from_the_evidence()
    {
        var b = Bundle(Dim("D1", DimensionCategory.CodeQuality, 8), Dim("AX1", DimensionCategory.Architecture, 9))
            with { HeadlineScore = 99 };
        var v = CaiScorer.Verify(b);
        Assert.False(v.Reproduced);
        Assert.True(v.Delta > 0.5);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var b = Bundle(Dim("D1", DimensionCategory.CodeQuality, 7.2, coverage: 0.9),
            Dim("AX1", DimensionCategory.Architecture, 6.4)) with { QualityBar = "mission-critical" };
        var back = EvidenceBundle.Parse(b.ToJson());
        Assert.Equal(CaiScorer.Score(b).Headline, CaiScorer.Score(back).Headline, 6);
    }

    [Fact]
    public void Out_of_range_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CaiScorer.Score(Bundle(Dim("D1", DimensionCategory.CodeQuality, 11))));
        Assert.Throws<ArgumentException>(() =>
            CaiScorer.Score(Bundle(Dim("D1", DimensionCategory.CodeQuality, 5, coverage: 1.5))));
        Assert.Throws<ArgumentException>(() => Categories.Parse("not-a-category"));
    }
}
