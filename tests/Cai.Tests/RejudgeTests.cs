using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The re-judge: does the standard's own judging reproduce?
/// </summary>
/// <remarks>
/// <para>★★ THE CHECK THAT POINTS AT US, NOT AT THE VENDOR. Every other verification asks whether a run
/// answered the holdout it claims. This one asks whether the INSTRUMENT is stable: judge a sample again,
/// independently, and see whether the second pass reaches the same answers. A rate produced by a process that
/// does not reproduce is not a measurement, however carefully the corpus was drawn — and CAI is the participant
/// that owns the judging, so this is the check a critic would ask for first.</para>
///
/// <para>★★ THE SAMPLE IS CHOSEN BY THE SEED, NEVER BY ANYBODY. A sample the judged party selects is not a
/// check; a sample selected by whoever runs the re-judge is not one either. It comes from the period's own
/// holdout seed, so it is reproducible by a third party from published values and cannot be steered towards the
/// findings that happen to agree.</para>
///
/// <para>★★ BINARY FOLD, AND THIS IS RATIFIED. Agreement is measured on noise vs not-noise: the noise KINDS are
/// beside the point for a rate taken over "noise or not". A pass that said <c>noise</c> where the second said
/// <c>both-wrong</c> agrees about the number and disagrees about the cause, and counting that as a disagreement
/// would report an instrument as unstable for classifying one defect two ways.</para>
/// </remarks>
public sealed class RejudgeTests
{
    private const string Seed = "cai-2026-09-9f2b41c7e0a85d36";

    private static IReadOnlyList<string> Findings(int n) =>
        [.. Enumerable.Range(1, n).Select(i => $"f{i:D4}")];

    // ── The sample ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_The_Sample_Is_Reproducible_From_The_Seed()
    {
        // ★★ A third party must be able to derive the same sample from published values, or "we re-judged a
        // sample" is a claim about a set only we can see.
        var a = Rejudge.SelectSample(Seed, "2026-09", Findings(200), size: 20);
        var b = Rejudge.SelectSample(Seed, "2026-09", Findings(200), size: 20);

        Assert.Equal(a, b);
        Assert.Equal(20, a.Count);
    }

    [Fact]
    public void STAR_A_Different_Period_Under_The_Same_Seed_Draws_A_Different_Sample()
    {
        // ★ Otherwise the same findings are re-judged every period, and a judging drift that only affects the
        // rest of the corpus is invisible for ever.
        var september = Rejudge.SelectSample(Seed, "2026-09", Findings(200), size: 20);
        var october = Rejudge.SelectSample(Seed, "2026-10", Findings(200), size: 20);

        Assert.NotEqual(september, october);
    }

    [Fact]
    public void STAR_The_Sample_Does_Not_Depend_On_THE_ORDER_The_Findings_Arrive_In()
    {
        // ★★ THE SUBTLE ONE. If input order moved the sample, whoever controls the query that lists the
        // findings controls which of them get re-judged — a steerable sample wearing a seed's clothes.
        var forward = Findings(200);
        var backward = forward.Reverse().ToList();

        Assert.Equal(
            Rejudge.SelectSample(Seed, "2026-09", forward, size: 20),
            Rejudge.SelectSample(Seed, "2026-09", backward, size: 20));
    }

    [Fact]
    public void A_Sample_Larger_Than_The_Population_Is_The_Whole_Population()
    {
        var sample = Rejudge.SelectSample(Seed, "2026-09", Findings(5), size: 20);

        Assert.Equal(5, sample.Count);
    }

    [Fact]
    public void No_Judged_Findings_Means_No_Sample()
    {
        Assert.Empty(Rejudge.SelectSample(Seed, "2026-09", [], size: 20));
    }

    // ── The comparison ────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string> Verdicts(params (string Id, string Verdict)[] pairs) =>
        pairs.ToDictionary(p => p.Id, p => p.Verdict, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void STAR_A_Second_Pass_That_Agrees_Is_Within_Tolerance()
    {
        var original = Verdicts(("f1", "valid-actionable"), ("f2", "noise"), ("f3", "both-wrong"));
        var outcome = Rejudge.Compare(["f1", "f2", "f3"], original, original);

        Assert.Equal(3, outcome.Compared);
        Assert.Equal(0, outcome.Disagreements);
        Assert.Equal(0d, outcome.DisagreementRate);
        Assert.True(outcome.WithinTolerance);
    }

    [Fact]
    public void STAR_The_NOISE_KINDS_Do_Not_Count_As_A_Disagreement()
    {
        // ★★ RATIFIED, and the reason the fold exists. `noise` ("it should not have fired") and `both-wrong`
        // ("neither reading is right") both score as NOISE — they agree about the number the rate is taken over
        // and disagree about the cause. Counting that as instability would report a stable instrument as
        // unstable for classifying one defect two ways, on a measure that never distinguished them.
        var outcome = Rejudge.Compare(
            ["f1", "f2"],
            Verdicts(("f1", "noise"), ("f2", "both-wrong")),
            Verdicts(("f1", "both-wrong"), ("f2", "noise")));

        Assert.Equal(0, outcome.Disagreements);
        Assert.True(outcome.WithinTolerance);
    }

    [Fact]
    public void STAR_The_ACTIONABILITY_Split_Is_Not_A_Disagreement_Either()
    {
        // ★ The other side of the same fold. "True and act on it" against "true but too thin to act on" both
        // score VALID; the actionability axis is published separately and is not what this check measures.
        var outcome = Rejudge.Compare(
            ["f1"],
            Verdicts(("f1", "valid-actionable")),
            Verdicts(("f1", "valid-not-actionable")));

        Assert.Equal(0, outcome.Disagreements);
        Assert.Equal(1, outcome.Compared);
    }

    [Fact]
    public void STAR_A_PROCESS_DEFECT_Leaves_The_Comparison_Rather_Than_Counting_As_Agreement()
    {
        // ★★ `cannot-tell` and `rubric-ambiguous` are not verdicts — they report a defect in OUR process, and
        // those items already leave the rate. Counting one as agreement would let a pass that gave up on half
        // the sample read as stable; counting it as disagreement would report our own thin evidence as an
        // unstable instrument. It is named and excluded, and the remaining comparison is over what was judged.
        var outcome = Rejudge.Compare(
            ["f1", "f2"],
            Verdicts(("f1", "noise"), ("f2", "cannot-tell")),
            Verdicts(("f1", "noise"), ("f2", "valid-actionable")));

        Assert.Equal(1, outcome.Compared);
        Assert.Equal(0, outcome.Disagreements);
        Assert.Contains("f2", outcome.Excluded);
        Assert.DoesNotContain("f2", outcome.Unjudged);
    }

    [Fact]
    public void STAR_Crossing_The_Noise_Boundary_IS_A_Disagreement()
    {
        // ★★ The one that matters: noise in one pass, valid in the other. That is the instrument moving the
        // number, which is the only kind of instability this check is for.
        var outcome = Rejudge.Compare(
            ["f1", "f2", "f3", "f4"],
            Verdicts(("f1", "noise"), ("f2", "valid-actionable"),
                     ("f3", "valid-actionable"), ("f4", "valid-actionable")),
            Verdicts(("f1", "valid-actionable"), ("f2", "valid-actionable"),
                     ("f3", "valid-actionable"), ("f4", "valid-actionable")));

        Assert.Equal(1, outcome.Disagreements);
        Assert.Equal(0.25, outcome.DisagreementRate);
    }

    [Fact]
    public void STAR_A_Rate_Above_The_TOLERANCE_Fails()
    {
        var outcome = Rejudge.Compare(
            ["f1", "f2", "f3", "f4"],
            Verdicts(("f1", "noise"), ("f2", "noise"),
                     ("f3", "valid-actionable"), ("f4", "valid-actionable")),
            Verdicts(("f1", "valid-actionable"), ("f2", "valid-actionable"),
                     ("f3", "valid-actionable"), ("f4", "valid-actionable")));

        Assert.Equal(0.5, outcome.DisagreementRate);
        Assert.False(outcome.WithinTolerance);
        Assert.True(Rejudge.Tolerance < 0.5);
    }

    [Fact]
    public void STAR_A_Sampled_Finding_The_Second_Pass_SKIPPED_Is_Not_Silently_Dropped()
    {
        // ★★ THE FAILURE MODE THAT LOOKS LIKE SUCCESS. Re-judge twenty, answer the three that agree, and a
        // naive rate over "compared" reports 0 % disagreement on a sample of three. The unanswered ones are
        // named, and an incomplete re-judge cannot be within tolerance however well the answered ones did.
        var outcome = Rejudge.Compare(
            ["f1", "f2", "f3"],
            Verdicts(("f1", "noise"), ("f2", "valid-actionable"), ("f3", "valid-actionable")),
            Verdicts(("f1", "noise")));

        Assert.Equal(1, outcome.Compared);
        Assert.Equal(2, outcome.Unjudged.Count);
        Assert.Contains("f2", outcome.Unjudged);
        Assert.False(outcome.WithinTolerance);
    }

    [Fact]
    public void STAR_An_UNRECOGNISED_Verdict_Is_Not_Folded_Into_Not_Noise()
    {
        // ★★ The default that would flatter us. An unparseable verdict silently treated as not-noise makes a
        // typo look like agreement — so it is refused as unusable rather than counted either way.
        var outcome = Rejudge.Compare(
            ["f1"],
            Verdicts(("f1", "noise")),
            Verdicts(("f1", "probably-fine")));

        Assert.Equal(0, outcome.Compared);
        Assert.Contains("f1", outcome.Unusable);
        Assert.False(outcome.WithinTolerance);
    }

    [Fact]
    public void An_Empty_Sample_Cannot_Be_Within_Tolerance()
    {
        // ★ Zero of zero is 0 % disagreement, which reads as a perfect result. "Nothing was re-judged" and
        // "the re-judge agreed" are opposite claims.
        var outcome = Rejudge.Compare([], Verdicts(), Verdicts());

        Assert.Equal(0, outcome.Compared);
        Assert.False(outcome.WithinTolerance);
    }

    [Fact]
    public void The_Tolerance_Is_Published_As_A_Number_Rather_Than_Buried()
    {
        Assert.InRange(Rejudge.Tolerance, 0.01, 0.25);
        Assert.False(string.IsNullOrWhiteSpace(Rejudge.ToleranceRationale));
    }
}
