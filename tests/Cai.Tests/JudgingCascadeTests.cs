using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The judging cascade: two judges, then two more blind, then a person.
/// </summary>
/// <remarks>
/// <para>★ The human stops being a measuring instrument and becomes an ADJUDICATOR. A 500-item audit
/// asked for forty to eighty hours of considered judgement and got a nine-second median — a race, not a
/// review. Spending people only where independent judges genuinely disagree puts roughly 6% of findings
/// in front of one, and every one is contested by construction.</para>
/// <para>The state machine lives in the standard so every participant resolves a disagreement the same
/// way. Two vendors applying different escalation rules would produce numbers that look comparable and
/// are not.</para>
/// </remarks>
public sealed class JudgingCascadeTests
{
    private static JudgeVote Vote(string judge, NoiseVerdict v) => new(judge, v);

    // ── Round one ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Two_judges_agreeing_settles_it()
    {
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.Noise), Vote("haiku", NoiseVerdict.Noise)], round2: []);

        Assert.Equal(CascadeState.Accepted, outcome.State);
        Assert.Equal(NoiseVerdict.Noise, outcome.Verdict);
        Assert.Equal(1, outcome.SettledAtRound);
    }

    /// <summary>
    /// ★ AGREEMENT IS ON THE BINARY. The noise classes overlap in practice, so requiring identical
    /// verdicts would manufacture disagreement about vocabulary and escalate findings nobody disagrees
    /// about.
    /// </summary>
    [Fact]
    public void STAR_agreement_is_on_the_binary_not_the_exact_verdict()
    {
        // Both say the finding stands; they differ only on whether a reader could act on it.
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.ValidAndActionable), Vote("haiku", NoiseVerdict.ValidNotActionable)],
            round2: []);

        Assert.Equal(CascadeState.Accepted, outcome.State);
        Assert.False(outcome.Verdict!.Value.IsNoise());
    }

    /// <summary>
    /// ★★ …BUT THE ACTIONABILITY IS THEN CONTESTED, and must not silently feed the second axis. The
    /// judges agreed the finding is valid and disagreed about whether anyone could act on it — picking
    /// one of them would publish an actionability figure nobody actually agreed on.
    /// </summary>
    [Fact]
    public void STAR_a_split_on_actionability_is_recorded_as_contested()
    {
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.ValidAndActionable), Vote("haiku", NoiseVerdict.ValidNotActionable)],
            round2: []);

        Assert.True(outcome.ActionabilityContested);
        Assert.Null(outcome.Actionable);
    }

    [Fact]
    public void Agreement_on_both_axes_carries_the_actionability_through()
    {
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.ValidNotActionable), Vote("haiku", NoiseVerdict.ValidNotActionable)],
            round2: []);

        Assert.False(outcome.ActionabilityContested);
        Assert.False(outcome.Actionable);
    }

    [Fact]
    public void Two_judges_disagreeing_go_to_a_second_round()
    {
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.Noise), Vote("haiku", NoiseVerdict.ValidAndActionable)],
            round2: []);

        Assert.Equal(CascadeState.NeedsRound2, outcome.State);
        Assert.Null(outcome.Verdict);
    }

    /// <summary>
    /// ★★ A MACHINE THAT CANNOT TELL ESCALATES — it never counts as agreement and never excuses the
    /// item. Treating it as an exclusion would hand the pipeline a way to duck its hardest cases and
    /// still report a clean rate.
    /// </summary>
    [Fact]
    public void STAR_a_judge_that_cannot_tell_escalates_rather_than_agreeing()
    {
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.CannotTell), Vote("haiku", NoiseVerdict.CannotTell)],
            round2: []);

        Assert.Equal(CascadeState.NeedsRound2, outcome.State);
    }

    /// <summary>An ambiguous rubric is a defect in the standard, and goes straight to a person.</summary>
    [Fact]
    public void A_judge_calling_the_rubric_ambiguous_goes_to_a_human()
    {
        var outcome = JudgingCascade.Resolve(
            [Vote("sonnet", NoiseVerdict.RubricAmbiguous), Vote("haiku", NoiseVerdict.Noise)],
            round2: []);

        Assert.Equal(CascadeState.NeedsHuman, outcome.State);
        Assert.Contains("rubric", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Round two ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_second_round_landing_on_one_side_settles_it()
    {
        var outcome = JudgingCascade.Resolve(
            round1: [Vote("sonnet", NoiseVerdict.Noise), Vote("haiku", NoiseVerdict.ValidAndActionable)],
            round2: [Vote("opus", NoiseVerdict.Noise), Vote("fable", NoiseVerdict.Noise)]);

        Assert.Equal(CascadeState.Accepted, outcome.State);
        Assert.Equal(NoiseVerdict.Noise, outcome.Verdict);
        Assert.Equal(2, outcome.SettledAtRound);
    }

    /// <summary>
    /// ★ A SPLIT SECOND ROUND GOES TO A PERSON. These are the genuinely contested findings — hard by
    /// construction — which is exactly where human attention is worth spending.
    /// </summary>
    [Fact]
    public void STAR_a_split_second_round_escalates_to_a_human()
    {
        var outcome = JudgingCascade.Resolve(
            round1: [Vote("sonnet", NoiseVerdict.Noise), Vote("haiku", NoiseVerdict.ValidAndActionable)],
            round2: [Vote("opus", NoiseVerdict.Noise), Vote("fable", NoiseVerdict.ValidAndActionable)]);

        Assert.Equal(CascadeState.NeedsHuman, outcome.State);
        Assert.Null(outcome.Verdict);
    }

    /// <summary>
    /// ★ Round two does NOT count round one's votes. It is a second independent read, not a tally over
    /// four — a majority across both rounds would let the first pair's split decide an outcome the second
    /// pair was convened precisely to settle.
    /// </summary>
    [Fact]
    public void STAR_round_two_decides_alone_rather_than_being_tallied_with_round_one()
    {
        // Round 1 leaned noise 1–1; round 2 says valid unanimously. The answer is VALID, not a 2–2 tie.
        var outcome = JudgingCascade.Resolve(
            round1: [Vote("sonnet", NoiseVerdict.Noise), Vote("haiku", NoiseVerdict.ValidAndActionable)],
            round2: [Vote("opus", NoiseVerdict.ValidAndActionable), Vote("fable", NoiseVerdict.ValidAndActionable)]);

        Assert.Equal(CascadeState.Accepted, outcome.State);
        Assert.False(outcome.Verdict!.Value.IsNoise());
    }

    // ── Shape ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ BLINDNESS IS A PROPERTY OF THE INPUT. A round-two judge is given the finding and nothing about
    /// round one — showing them the first pair's verdicts anchors the answer on whichever position was
    /// argued more fluently, and verbosity is not evidence. Asserted structurally, because the failure
    /// would be a prompt quietly gaining a field rather than anything visible in a result.
    /// </summary>
    [Fact]
    public void STAR_a_vote_carries_no_knowledge_of_any_other_round()
    {
        var names = typeof(JudgeVote).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["Judge", "Verdict"], names);
        foreach (var w in new[] { "Round", "Prior", "Other", "Previous" })
        {
            Assert.DoesNotContain(names, n => n.Contains(w, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_round_needs_exactly_two_votes()
    {
        Assert.Throws<ArgumentException>(() =>
            JudgingCascade.Resolve([Vote("sonnet", NoiseVerdict.Noise)], round2: []));
    }
}
