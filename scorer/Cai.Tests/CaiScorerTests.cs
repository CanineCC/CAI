using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

public sealed class CaiScorerTests
{
    private static EvidenceBundle Bundle(params LensScore[] lenses) =>
        new() { RubricVersion = "rubric-test", Lenses = lenses };

    // securityCompliance is worst (58) yet carries the most weight (0.24) — worst-first.
    private static EvidenceBundle Sample() => Bundle(
        new("securityCompliance", 58, 0.24),
        new("maturity", 65, 0.20),
        new("architecture", 72, 0.18),
        new("codeHealth", 86, 0.14),
        new("productionReadiness", 80, 0.12),
        new("accessibility", 90, 0.06),
        new("performance", 88, 0.06)) with { HeadlineScore = 72.2 };

    [Fact]
    public void Folds_the_shipped_weights_into_the_published_headline()
    {
        var s = CaiScorer.Score(Sample());
        Assert.Equal(72.2, s.Headline, precision: 6);
        Assert.Equal(Band.Strong, s.Band);
    }

    [Fact]
    public void Is_deterministic_same_evidence_same_number()
    {
        var a = CaiScorer.Score(Sample()).Headline;
        var b = CaiScorer.Score(Sample()).Headline;
        Assert.Equal(a, b); // byte-for-byte
    }

    [Fact]
    public void Verify_reproduces_a_correct_published_headline()
    {
        var v = CaiScorer.Verify(Sample());
        Assert.True(v.Reproduced);
        Assert.True(v.Delta < 0.001);
    }

    [Fact]
    public void Verify_rejects_a_headline_that_does_not_follow_from_the_evidence()
    {
        var tampered = Sample() with { HeadlineScore = 90 }; // claims Exemplary; evidence folds to 72.2
        var v = CaiScorer.Verify(tampered);
        Assert.False(v.Reproduced);
        Assert.True(v.Delta > 0.5);
    }

    [Theory]
    [InlineData(95, Band.Exemplary)]
    [InlineData(90, Band.Exemplary)]
    [InlineData(89.9, Band.Strong)]
    [InlineData(70, Band.Strong)]
    [InlineData(50, Band.Adequate)]
    [InlineData(25, Band.Weak)]
    [InlineData(24.9, Band.Critical)]
    [InlineData(0, Band.Critical)]
    public void Bands_at_the_canonical_thresholds(double score, Band expected) =>
        Assert.Equal(expected, Bands.For(score));

    [Fact]
    public void Reference_worst_first_weights_put_the_most_weight_on_the_weakest_lens()
    {
        var w = CaiScorer.ReferenceWorstFirstWeights([86, 58, 72]); // worst = 58 (index 1)
        Assert.Equal(1.0, w.Sum(), precision: 9);
        Assert.True(w[1] > w[2] && w[2] > w[0]); // 58 > 72 > 86 in weight
    }

    [Fact]
    public void Falls_back_to_the_reference_vector_when_no_weights_are_shipped()
    {
        // No owaWeight (all 0) ⇒ the reference worst-first vector is used; still deterministic + banded.
        var noWeights = Bundle(
            new("codeHealth", 90, 0),
            new("securityCompliance", 40, 0),
            new("architecture", 70, 0));
        var s = CaiScorer.Score(noWeights);
        // worst-first: 40 gets 3/6, 70 gets 2/6, 90 gets 1/6 ⇒ (40*3 + 70*2 + 90*1)/6 = 350/6 ≈ 58.33
        Assert.Equal(58.333, s.Headline, precision: 2);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var json = Sample().ToJson();
        var back = EvidenceBundle.Parse(json);
        Assert.Equal(CaiScorer.Score(Sample()).Headline, CaiScorer.Score(back).Headline, precision: 6);
    }
}
