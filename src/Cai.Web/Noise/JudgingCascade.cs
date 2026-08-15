namespace Cai.Web.Noise;

/// <summary>
/// One judge's verdict on one finding.
/// </summary>
/// <remarks>
/// ★★ It carries NO knowledge of any other round, and that absence is the design. A round-two judge is
/// given the finding and nothing about round one — showing them the first pair's verdicts anchors the
/// answer on whichever position was argued more fluently, and verbosity is not evidence. The failure
/// mode would be a prompt quietly gaining a field, invisible in any result, so the shape is asserted
/// structurally in the tests.
/// </remarks>
public sealed record JudgeVote(string Judge, NoiseVerdict Verdict);

/// <summary>Where a finding got to in the cascade.</summary>
public enum CascadeState
{
    /// <summary>Judges agreed; the verdict stands without a person.</summary>
    Accepted,

    /// <summary>Round one disagreed. Two further judges rate it BLIND.</summary>
    NeedsRound2,

    /// <summary>Genuinely contested, or the rubric is at fault. A person decides.</summary>
    NeedsHuman,
}

/// <summary>What the cascade concluded, and why.</summary>
/// <param name="ActionabilityContested">
/// ★ The judges agreed the finding is valid and disagreed about whether anyone could act on it. The
/// binary settles, but the actionability does NOT — picking one judge's view would publish a figure
/// nobody agreed on, so the item is left out of the actionability axis instead.
/// </param>
public sealed record CascadeOutcome(
    CascadeState State,
    NoiseVerdict? Verdict,
    int SettledAtRound,
    bool ActionabilityContested,
    bool? Actionable,
    string Reason);

/// <summary>
/// Two judges, then two more blind, then a person.
/// </summary>
/// <remarks>
/// <para>★ The human stops being a measuring instrument and becomes an ADJUDICATOR. A 500-item audit
/// asked for forty to eighty hours of considered judgement and got a nine-second median — a race, not a
/// review. Spending people only where independent judges genuinely disagree puts roughly 6% of findings
/// in front of one, and each is contested by construction, so there is nothing to race through.</para>
/// <para>The state machine lives in the standard so every participant resolves a disagreement the same
/// way. Two vendors applying different escalation rules would produce numbers that look comparable and
/// are not — which is the failure a shared method exists to prevent.</para>
/// </remarks>
public static class JudgingCascade
{
    /// <summary>Resolve a finding from its votes. Pure: no clock, no model, no I/O.</summary>
    /// <param name="round1">Exactly two independent votes.</param>
    /// <param name="round2">Exactly two further BLIND votes, or empty when round one settled it.</param>
    public static CascadeOutcome Resolve(IReadOnlyList<JudgeVote> round1, IReadOnlyList<JudgeVote> round2)
    {
        ArgumentNullException.ThrowIfNull(round1);
        ArgumentNullException.ThrowIfNull(round2);

        if (round1.Count != 2)
        {
            throw new ArgumentException(
                "a round is exactly two independent judges — one is not a cascade, and three invites a "
                + "majority that hides a genuine split.", nameof(round1));
        }

        // An ambiguous rubric is a defect in the STANDARD, not a disagreement about the finding. A
        // second pair reading the same ambiguous rule cannot resolve it, so it goes straight to a
        // person, who can say what the rule should have said.
        if (round1.Any(v => v.Verdict == NoiseVerdict.RubricAmbiguous))
        {
            return new(CascadeState.NeedsHuman, null, 0, false, null,
                "a judge reports the rubric is ambiguous here — a second pair reading the same rule "
                + "cannot settle that, so it is a specification defect for a person to resolve.");
        }

        var settled = Settle(round1);
        if (settled is not null)
        {
            return settled with { SettledAtRound = 1 };
        }

        if (round2.Count == 0)
        {
            return new(CascadeState.NeedsRound2, null, 0, false, null,
                "the first two judges disagreed on the noise boundary — two further judges rate it "
                + "BLIND, without sight of these verdicts.");
        }

        if (round2.Count != 2)
        {
            throw new ArgumentException("a round is exactly two independent judges.", nameof(round2));
        }

        // ★ Round two decides ALONE. Tallying it with round one would let the first pair's split help
        // decide the very question the second pair was convened to settle, and a 2–2 across four judges
        // would send to a human a finding the independent second read agreed on unanimously.
        var second = Settle(round2);
        return second is not null
            ? second with { SettledAtRound = 2 }
            : new(CascadeState.NeedsHuman, null, 0, false, null,
                "both rounds split — this finding is genuinely contested, which is exactly where a "
                + "person's attention is worth spending.");
    }

    /// <summary>One round's conclusion, or null when it did not settle.</summary>
    private static CascadeOutcome? Settle(IReadOnlyList<JudgeVote> round)
    {
        var a = round[0].Verdict;
        var b = round[1].Verdict;

        // ★ "Cannot tell" from a MACHINE never counts as agreement and never excuses the item — treating
        // it as an exclusion would hand the pipeline a way to duck its hardest cases and still report a
        // clean rate.
        if (a == NoiseVerdict.CannotTell || b == NoiseVerdict.CannotTell)
        {
            return null;
        }

        // ★ Agreement is on the BINARY. The noise classes overlap in practice — redundant,
        // opinion-not-fact and shape-irrelevant are frequently three readings of one finding — so
        // requiring identical verdicts would manufacture disagreement about vocabulary and escalate
        // findings nobody actually disputes.
        if (a.IsNoise() != b.IsNoise())
        {
            return null;
        }

        // The binary settled. Actionability is a SEPARATE axis and may still be contested — in which
        // case it is left out rather than decided by picking a judge.
        var actionableA = a.IsActionable();
        var actionableB = b.IsActionable();
        var contested = actionableA != actionableB;

        return new(
            CascadeState.Accepted,
            // The stricter of the two on the second axis, so an accepted verdict never claims a finding
            // is more actionable than a judge thought it was.
            a.IsNoise() ? a : (actionableA == false || actionableB == false ? NoiseVerdict.ValidNotActionable : a),
            SettledAtRound: 0,
            ActionabilityContested: contested,
            Actionable: contested ? null : actionableA,
            Reason: contested
                ? "the judges agreed on the noise boundary and split on actionability, so the finding "
                  + "scores and the actionability axis leaves it out."
                : "the judges agreed.");
    }
}
