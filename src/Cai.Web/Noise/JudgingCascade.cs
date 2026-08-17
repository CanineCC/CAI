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

/// <summary>
/// Whether a panel may be RECORDED — the shape 02 §2 requires, checked once.
/// </summary>
/// <remarks>
/// <para>★★ "A BLIND SPOT LIVES IN THE WEIGHTS; NO REPHRASING REMOVES IT. A single-family ensemble cannot see a
/// single-family blind spot." The cascade recorded whatever it was handed: four votes from one model under four
/// judge names would have been stored as a judgement, and the record would have shown four agreeing judges where
/// there was one opinion counted four times.</para>
///
/// <para>★★ AN UNDECLARED FAMILY IS NOT A DIFFERENT FAMILY, and an undeclared temperature is not zero. Either
/// default would let a panel pass by OMITTING the field, which enforces the requirement only against submitters who
/// filled it in honestly.</para>
///
/// <para>★ A pure function over the declarations, so it is testable without a database and so the resolver stays a
/// calculation: this constrains RECORDING, not arithmetic.</para>
/// </remarks>
public static class JudgePanel
{
    /// <summary>
    /// Distinct models the FULL panel has, when round two convened.
    /// </summary>
    /// <remarks>
    /// ★★ NOT A MINIMUM PER JUDGEMENT — and this is a deliberate reading of #10, recorded in 06-decisions.
    /// "Four distinct models across the two rounds" describes the full panel, and requiring four to RECORD would
    /// contradict the cascade's own design: round two convenes only when round one has actually split, so an
    /// efficient round-one settle — which is most findings — could never be recorded at all. The enforceable rule
    /// with the same effect is <b>no model may appear twice in a panel</b> plus <b>at least two families</b>: a
    /// round-one pair is then two distinct models from two traditions, and a round-two panel is four.
    /// </remarks>
    public const int FullPanelDistinctModels = 4;

    /// <summary>Distinct declared families the method requires.</summary>
    /// <remarks>★ Two is the floor that makes the ensemble cross-family at all; more is better and not required.</remarks>
    public const int RequiredFamilies = 2;

    /// <summary>The only temperature a re-runnable verdict can have been produced at.</summary>
    public const double RequiredTemperature = 0;

    /// <summary>Why the shape is what it is, published with it.</summary>
    public const string Why =
        "A blind spot lives in the weights and no rephrasing removes it, so a single-family ensemble cannot see a "
      + "single-family blind spot: four models from one vendor is one training tradition, and their agreement says "
      + "nothing about all four being wrong the same way. Determinism is part of the same promise — a verdict "
      + "produced above temperature 0 cannot be re-run to the same answer, which makes 'anyone may re-run the "
      + "judges' false for it.";

    /// <summary>One judge's declaration, as far as the panel check is concerned.</summary>
    public sealed record Declaration(string Judge, string? Model, string? Family, double? Temperature);

    /// <summary>
    /// Everything wrong with a panel — empty when it may be recorded.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<Declaration> panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        var problems = new List<string>();

        var declared = panel.Where(p => !string.IsNullOrWhiteSpace(p.Model)).ToList();
        var models = declared
            .Select(p => p.Model)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ★★ NO MODEL TWICE. Four votes from one model are one opinion counted four times, and the record would
        // show four agreeing judges where there was one. See FullPanelDistinctModels for why this is the rule
        // rather than a flat minimum of four.
        if (models.Count < declared.Count)
        {
            var repeated = declared
                .GroupBy(p => p.Model, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(p => p.Judge))})");

            problems.Add(
                "the panel votes the same model more than once: " + string.Join("; ", repeated)
              + ". That is one opinion counted twice, and the record would show two agreeing judges where there "
              + "was one. The method requires distinct models — four across the two rounds when round two "
              + "convenes.");
        }

        // ★★ An UNDECLARED family is not a different family — see the class remarks.
        var undeclared = panel.Where(p => string.IsNullOrWhiteSpace(p.Family)).Select(p => p.Judge).ToList();
        if (undeclared.Count > 0)
        {
            problems.Add(
                "these judges declare no model family: " + string.Join(", ", undeclared)
              + ". An undeclared family cannot count as a different one, or every panel would pass by omitting "
              + "the field.");
        }

        var families = panel
            .Select(p => p.Family)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (undeclared.Count == 0 && families.Count < RequiredFamilies)
        {
            problems.Add(
                $"the panel spans {families.Count} model family (" + string.Join(", ", families)
              + $") and the method requires at least {RequiredFamilies}. A blind spot lives in the weights: a "
              + "single-family ensemble cannot see a single-family blind spot, however many models it has.");
        }

        foreach (var judge in panel.Where(p => p.Temperature is null))
        {
            problems.Add(
                $"judge '{judge.Judge}' declares no temperature. An undeclared temperature cannot be assumed to "
              + "be 0, or determinism would be enforced only against the honest.");
        }

        foreach (var judge in panel.Where(p => p.Temperature is { } t && Math.Abs(t) > 1e-9))
        {
            problems.Add(
                $"judge '{judge.Judge}' declares temperature {judge.Temperature}. A verdict produced above 0 "
              + "cannot be re-run to the same answer, so 'anyone may re-run the judges and get the same answers' "
              + "would be false for it.");
        }

        return problems;
    }
}

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
