using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

public sealed class CaiScorerTests
{
    private static DimensionScore Dim(string id, string lens, double score, double conf = 1.0, bool advisory = false) =>
        new(id, lens, score, conf) { Advisory = advisory };

    private static EvidenceBundle Dims(params DimensionScore[] dims) =>
        new() { RubricVersion = "rubric-test", Dimensions = dims };

    [Fact]
    public void Folds_dimensions_through_within_then_across_lens_owa()
    {
        // codeHealth [8,6] → within-lens OWA q=0.75 → 6.857/10 → 68.57; architecture [9] → 90.
        // across-lens OWA q=0.55 over [68.57, 90] → 76.18.
        var s = CaiScorer.Score(Dims(
            Dim("D1", "codeHealth", 8), Dim("D2", "codeHealth", 6),
            Dim("D5", "architecture", 9)));

        Assert.Equal(76.2, s.Headline, 1);
        Assert.Equal(Band.Healthy, s.Band);
        var ch = s.Lenses.Single(l => l.Lens == "codeHealth");
        Assert.Equal(68.57, ch.Score, 1);
        Assert.Equal(2, ch.DimensionCount);
        Assert.Equal(90, s.Lenses.Single(l => l.Lens == "architecture").Score, 1);
    }

    [Fact]
    public void Worst_dimension_carries_the_most_weight_within_a_lens()
    {
        // Same dimensions, the weak one swapped which is worst — the worst always drags hardest.
        var weakArch = CaiScorer.Score(Dims(Dim("a", "architecture", 1), Dim("b", "architecture", 9))).Headline;
        var strongArch = CaiScorer.Score(Dims(Dim("a", "architecture", 7), Dim("b", "architecture", 9))).Headline;
        Assert.True(weakArch < strongArch);
        // a 1 and a 9 do NOT average to 50 — the worst is over-weighted (folds to ~44).
        Assert.True(weakArch < 50);
    }

    [Fact]
    public void Critical_gate_caps_a_lens_band_at_Fair_without_changing_the_number()
    {
        // A 3.5/10 dimension is below the 4.0 gate; the lens still scores into Healthy but its band caps at Fair.
        var s = CaiScorer.Score(Dims(
            Dim("x", "codeHealth", 3.5), Dim("y", "codeHealth", 10), Dim("z", "codeHealth", 10)));
        var ch = s.Lenses.Single(l => l.Lens == "codeHealth");
        Assert.True(ch.CriticalGated);
        Assert.True(ch.Score >= 70);          // the NUMBER is unchanged (Healthy range)
        Assert.Equal(Band.Fair, ch.Band);     // the displayed BAND is capped
    }

    [Fact]
    public void Advisory_and_unmeasured_dimensions_are_excluded_from_the_number()
    {
        var withNoise = CaiScorer.Score(Dims(
            Dim("real", "codeHealth", 8),
            Dim("llm", "codeHealth", 1, advisory: true),  // advisory — must not drag
            Dim("unmeasured", "codeHealth", 0, conf: 0))).Headline; // confidence 0 — not measured
        var clean = CaiScorer.Score(Dims(Dim("real", "codeHealth", 8))).Headline;
        Assert.Equal(clean, withNoise, 6);
    }

    [Fact]
    public void Is_deterministic()
    {
        var b = Dims(Dim("D1", "codeHealth", 7.3), Dim("D5", "architecture", 6.1), Dim("D8", "maturity", 8.4));
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
        Assert.True(w[1] > w[2] && w[2] > w[0]); // 58 > 72 > 86 in weight
    }

    [Fact]
    public void Lens_only_bundle_with_shipped_weights_reproduces_exactly()
    {
        // The thin-sidecar fallback: lens scores + shipped OWA weights fold to the published headline.
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
        var b = Dims(Dim("D1", "codeHealth", 8), Dim("D5", "architecture", 9)) with { HeadlineScore = 99 };
        var v = CaiScorer.Verify(b);
        Assert.False(v.Reproduced);
        Assert.True(v.Delta > 0.5);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var b = Dims(Dim("D1", "codeHealth", 7.2), Dim("D5", "architecture", 6.4));
        var back = EvidenceBundle.Parse(b.ToJson());
        Assert.Equal(CaiScorer.Score(b).Headline, CaiScorer.Score(back).Headline, 6);
    }
}
