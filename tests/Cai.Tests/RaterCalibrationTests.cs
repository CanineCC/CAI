using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Calibrating a rater against findings whose answer is already settled — by evidence, not by opinion.
/// </summary>
/// <remarks>
/// <para>★★ A honeypot is EARNED, never decreed. The obvious construction — take findings the crowd
/// agreed on and score people against that — measures conformity and calls it accuracy. It would rank a
/// rater highest for repeating the consensus and lowest for catching what everyone else missed, which is
/// the single most valuable thing a crowd can produce.</para>
/// <para>So a honeypot's truth must come from OUTSIDE the rating process: a fix merged upstream, a
/// vendor withdrawing the rule, an advisory retracted. Each is a fact about the world that would still be
/// true if no one had ever rated anything.</para>
/// </remarks>
public sealed class RaterCalibrationTests
{
    private static Honeypot Pot(string id, NoiseVerdict truth, HoneypotSource source = HoneypotSource.UpstreamFixMerged) =>
        new(id, truth, source, "https://github.com/acme/thing/pull/1");

    private static CrowdAnswer Answer(string rater, string finding, NoiseVerdict verdict) =>
        new(finding, rater, verdict, MachineVerdict: null);

    // ── What may become a honeypot ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ CROWD CONSENSUS CANNOT EARN IT. Scoring raters against what the crowd agreed measures
    /// conformity: the rater who repeats the majority scores highest, and the one who spots what everyone
    /// missed scores lowest. It is circular — the crowd would be validating itself — and the direction of
    /// the error is towards agreement, which is exactly the failure the crowd exists to break.
    /// </summary>
    [Fact]
    public void STAR_crowd_consensus_is_not_an_earned_source()
    {
        Assert.Null(RaterCalibration.ParseSource("crowd-consensus"));
        Assert.Null(RaterCalibration.ParseSource("majority"));
        Assert.Null(RaterCalibration.ParseSource("obvious"));
    }

    [Theory]
    [InlineData("upstream-fix-merged", HoneypotSource.UpstreamFixMerged)]
    [InlineData("vendor-withdrew", HoneypotSource.VendorWithdrew)]
    [InlineData("advisory-retracted", HoneypotSource.AdvisoryRetracted)]
    public void An_external_settlement_earns_it(string wire, HoneypotSource expected)
    {
        Assert.Equal(expected, RaterCalibration.ParseSource(wire));
    }

    /// <summary>
    /// ★ Evidence is required, and it is a LINK — something a third party can open. "We checked" is not
    /// evidence; it is the same claim the honeypot is supposed to be independent of.
    /// </summary>
    [Fact]
    public void A_honeypot_without_evidence_is_refused()
    {
        Assert.False(RaterCalibration.IsWellFormed(
            new Honeypot("f1", NoiseVerdict.Noise, HoneypotSource.VendorWithdrew, Evidence: null)));
        Assert.False(RaterCalibration.IsWellFormed(
            new Honeypot("f1", NoiseVerdict.Noise, HoneypotSource.VendorWithdrew, Evidence: "we checked")));
        Assert.True(RaterCalibration.IsWellFormed(Pot("f1", NoiseVerdict.Noise)));
    }

    // ── Scoring a rater ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_rater_is_scored_on_the_honeypots_they_answered()
    {
        List<Honeypot> pots =
        [
            Pot("h1", NoiseVerdict.Noise), Pot("h2", NoiseVerdict.Noise),
            Pot("h3", NoiseVerdict.ValidAndActionable), Pot("h4", NoiseVerdict.ValidAndActionable),
            Pot("h5", NoiseVerdict.Noise),
        ];
        List<CrowdAnswer> answers =
        [
            Answer("r1", "h1", NoiseVerdict.Noise),
            Answer("r1", "h2", NoiseVerdict.Noise),
            Answer("r1", "h3", NoiseVerdict.ValidAndActionable),
            Answer("r1", "h4", NoiseVerdict.Noise),          // wrong
            Answer("r1", "h5", NoiseVerdict.Noise),
            Answer("r1", "real-finding", NoiseVerdict.Noise), // not a honeypot; not scored
        ];

        var score = RaterCalibration.Score(answers, pots).Single(s => s.RaterId == "r1");

        Assert.Equal(5, score.Answered);
        Assert.Equal(4, score.Agreed);
        Assert.Equal(0.8, score.Accuracy!.Value, 3);
    }

    /// <summary>
    /// ★ Agreement is on the BINARY — noise or not. The six verdicts overlap in practice: "valid but not
    /// actionable" and "valid and actionable" are a judgement about the fix, not about whether the tool
    /// was right to fire, and scoring a rater down for that would penalise a distinction the honeypot's
    /// evidence usually cannot settle either.
    /// </summary>
    [Fact]
    public void STAR_agreement_is_on_the_binary_not_the_exact_verdict()
    {
        var score = RaterCalibration.Score(
            [Answer("r1", "h1", NoiseVerdict.ValidNotActionable)],
            [Pot("h1", NoiseVerdict.ValidAndActionable)]).Single();

        Assert.Equal(1, score.Agreed);
    }

    /// <summary>
    /// ★★ ONE FOR ONE IS NOT 100%. Below the minimum sample the accuracy is NULL — not zero, not
    /// perfect — because a figure computed on two answers will be read as a rating, and a rater dropped
    /// or promoted on two answers is noise being treated as signal.
    /// </summary>
    [Fact]
    public void STAR_below_the_minimum_sample_there_is_no_accuracy_figure()
    {
        var score = RaterCalibration.Score(
            [Answer("r1", "h1", NoiseVerdict.Noise)], [Pot("h1", NoiseVerdict.Noise)]).Single();

        Assert.Equal(1, score.Answered);
        Assert.Equal(1, score.Agreed);
        Assert.Null(score.Accuracy);
        Assert.False(score.Calibrated);
    }

    [Fact]
    public void A_rater_who_has_answered_no_honeypot_is_uncalibrated_not_perfect()
    {
        var scores = RaterCalibration.Score([Answer("r1", "real", NoiseVerdict.Noise)], [Pot("h1", NoiseVerdict.Noise)]);

        var r1 = scores.Single(s => s.RaterId == "r1");
        Assert.Equal(0, r1.Answered);
        Assert.Null(r1.Accuracy);
        Assert.False(r1.Calibrated);
    }

    // ── What calibration may and may not do ───────────────────────────────────────────────────────

    /// <summary>
    /// ★★ CALIBRATION NEVER DELETES AN ANSWER. Dropping the answers of raters who scored badly is
    /// selection on the outcome: the excluded rater is chosen using the very variable being measured, and
    /// what survives is the subset that agreed — a cleaner number that means less. The scores are
    /// PUBLISHED so a reader can weigh them; the answers stay in the denominator either way.
    /// </summary>
    [Fact]
    public void STAR_a_poor_score_does_not_remove_that_raters_answers()
    {
        List<Honeypot> pots = [.. Enumerable.Range(0, 5).Select(i => Pot($"h{i}", NoiseVerdict.Noise))];
        List<CrowdAnswer> answers =
        [
            .. Enumerable.Range(0, 5).Select(i => Answer("bad", $"h{i}", NoiseVerdict.ValidAndActionable)),
            Answer("bad", "real-finding", NoiseVerdict.Noise),
        ];

        var kept = RaterCalibration.Retain(answers, RaterCalibration.Score(answers, pots));

        Assert.Equal(answers.Count, kept.Count);
    }

    /// <summary>
    /// ★ A honeypot answer is not evidence about the finding — its answer was already known, so counting
    /// it in the noise rate would measure the honeypot mixture rather than the tool.
    /// </summary>
    [Fact]
    public void STAR_honeypot_answers_are_excluded_from_the_measurement_they_calibrate()
    {
        List<CrowdAnswer> answers = [Answer("r1", "h1", NoiseVerdict.Noise), Answer("r1", "real", NoiseVerdict.Noise)];

        var measured = RaterCalibration.ExcludeHoneypots(answers, [Pot("h1", NoiseVerdict.Noise)]);

        Assert.Equal(["real"], measured.Select(a => a.FindingId));
    }

    /// <summary>
    /// ★★ A rater cannot tell a honeypot from a real question — it flows through the same queue and the
    /// same view. A calibration item you can recognise measures how carefully someone answers when
    /// watched, which is not the quantity anyone wants.
    /// </summary>
    [Fact]
    public void STAR_nothing_on_the_honeypot_type_reaches_the_raters_view()
    {
        var names = typeof(CrowdItemView).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["FindingId"], names);
        foreach (var w in new[] { "Honeypot", "Truth", "Calibration", "Source" })
        {
            Assert.DoesNotContain(names, n => n.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
    }
}
