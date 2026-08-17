using System.Globalization;

namespace Cai.Web.Noise;

/// <summary>Why a publication was refused, in the words a reader of the refusal needs.</summary>
/// <param name="Field">The wire field at fault, so a submitter can act without guessing.</param>
/// <param name="Error">What is wrong and why the standard cares.</param>
public sealed record ContractBreach(string Field, string Error);

/// <summary>
/// The gate between "a number was computed" and "a number may publish".
/// </summary>
/// <remarks>
/// <para>★★ IT EXISTS BECAUSE <c>/api/noise/method</c> WAS PUBLISHING A CONTRACT NOTHING ENFORCED. The method
/// endpoint listed ten fields as <c>requiredWithEveryRate</c> — the LoC absolutes, a recall estimate with its
/// method named, the claim-class breakdown, the provenance — and the publication endpoint accepted a request
/// that had nowhere to put most of them and checked two. A rule published without enforcement is worse than an
/// absent rule: it is a promise a reader will assume was kept.</para>
///
/// <para>★★ AND THE EXCLUSION CEILING NOW FIRES. <c>MaxExclusionRate</c> was declared as a constant, echoed
/// at <c>/method</c> as though it governed something, and compared against nothing. The specification is
/// explicit that combined exclusions above the ceiling <b>void the run</b> — not a pass with a caveat, not a
/// verdict on the tool, but an instrument unfit to have been run. Exclusions concentrate where the evidence is
/// thin, which is where judging is worst, so a run that lost a tenth of its findings to "can't tell" is
/// flattered by exactly the items it dropped.</para>
///
/// <para>★ Every breach is returned at once. Told one at a time, a submitter fixes six things over six
/// round-trips and learns nothing about the shape of the contract.</para>
/// </remarks>
public static class PublicationContract
{
    /// <summary>
    /// Combined exclusions above this share void the run.
    /// </summary>
    /// <remarks>★ The single definition. It used to live on the endpoint, where it was echoed to readers and
    /// applied to nothing.</remarks>
    public const double MaxExclusionRate = 0.05;

    /// <summary>The recall methods the standard recognises, in the order 04 recommends them.</summary>
    /// <remarks>
    /// ★★ A recall estimate must NAME its method, because the five available methods do not measure the same
    /// thing and are not interchangeable. "Recall: 80 %" with no method is a number a reader cannot weigh:
    /// against a multi-vendor union it is a strong claim, against a planted-defect corpus it is a regression
    /// floor, and against nothing at all it is a guess.
    /// </remarks>
    public static readonly IReadOnlyList<string> RecallMethods =
    [
        "pooled-union",       // 04 fix #2 — the union of what participating tools reported and a human validated
        "gap-backlog",        // 04 fix #1 — gaps discovered later, per unit of scanning effort
        "longitudinal",       // 04 fix #3 — a fix at HEAD for something never flagged
        "planted-corpus",     // 04 fix #4 — a regression floor, not a recall estimate
        "blind-human-first",  // 04 fix #5 — the only method that finds what nobody thought to look for
        "none",               // ★ Declared absence, with a reason. Honest, and it publishes as absent.
    ];

    /// <summary>True when the method is one the standard knows.</summary>
    public static bool IsKnownRecallMethod(string? method) =>
        method is { Length: > 0 } && RecallMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The exclusion share of everything that reached the funnel, or null when nothing did.
    /// </summary>
    public static double? ExclusionRate(int adjudicated, int excluded)
    {
        var seen = adjudicated + excluded;
        return seen == 0 ? null : (double)excluded / seen;
    }

    /// <summary>Whether the run is void on exclusions alone.</summary>
    public static bool VoidOnExclusions(int adjudicated, int excluded) =>
        ExclusionRate(adjudicated, excluded) is { } rate && rate > MaxExclusionRate;

    /// <summary>
    /// Everything wrong with a submitted publication, in one pass.
    /// </summary>
    /// <param name="loc">
    /// Lines of code the run covered — the denominator of the absolutes. ★★ Required, because the RATIO hides
    /// suppression and the absolutes expose it: a tool at 42 valid / 8 noise per 100k LoC has a worse ratio
    /// than one at 12 valid / 2 noise, and is plainly the better instrument. A ratio whose denominator the
    /// measured party controls is not a headline.
    /// </param>
    /// <param name="recallEstimate">The recall counterpart, or null when the method is "none".</param>
    /// <param name="recallMethod">Which of <see cref="RecallMethods"/> produced it.</param>
    /// <param name="recallNote">Required when the method is "none" — the absence publishes with its reason.</param>
    /// <param name="claims">The claim-class breakdown. Empty is a breach, not a default.</param>
    /// <param name="toolVersion">Provenance: what was run.</param>
    /// <param name="holdoutSeed">Provenance: which draw it was run against.</param>
    /// <param name="modelSet">Provenance: which judges, with versions.</param>
    /// <param name="gitMiningVerified">
    /// ★★ THE PRE-PUBLICATION GATE FROM 05. A contained scan without a readable <c>.git</c> makes the
    /// history-derived dimensions emit false verdicts. If a measured run was configured that way, its noise on
    /// exactly the dimensions that face behavioural-analysis competitors is an ENVIRONMENT ARTEFACT, and
    /// publishing it would be reporting our own harness bug as a product weakness. It is checkable against a
    /// run's configuration, so it is a gate rather than a caveat added afterwards. Null means "not declared",
    /// which fails: a run that cannot say is a run that did not check.
    /// </param>
    public static IReadOnlyList<ContractBreach> Check(
        long? loc,
        double? recallEstimate, string? recallMethod, string? recallNote,
        IReadOnlyCollection<ClaimClassTally> claims,
        string? toolVersion, string? holdoutSeed, string? modelSet,
        bool? gitMiningVerified,
        int adjudicated, int excluded,
        bool hasFixRateObservations, string? fixRateUnavailable, int? fixRateWindowDays)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var breaches = new List<ContractBreach>();

        // ── The absolutes ────────────────────────────────────────────────────────────────────────
        if (loc is not > 0)
        {
            breaches.Add(new ContractBreach("locCovered",
                "the run must state the LoC it covered. Every rate here is also published per 100k LoC, "
              + "because the ratio hides suppression and the absolutes expose it: 42 valid / 8 noise per "
              + "100k has a worse ratio than 12 valid / 2 noise and is plainly the better instrument. A "
              + "ratio whose denominator the measured party controls is not a headline."));
        }

        // ── The recall counterpart ───────────────────────────────────────────────────────────────
        if (!IsKnownRecallMethod(recallMethod))
        {
            breaches.Add(new ContractBreach("recallMethod",
                "a recall estimate must NAME its method, and the five available methods do not measure the "
              + "same thing. Send one of: " + string.Join(", ", RecallMethods) + ". A noise rate is a "
              + "PRECISION measure; published alone it rewards reporting less, across every tool that "
              + "adopts the standard."));
        }
        else if (string.Equals(recallMethod, "none", StringComparison.OrdinalIgnoreCase))
        {
            // ★ Absence is allowed and publishes AS absence — with a reason a reader can weigh. What is not
            // allowed is silence, which reads as "recall was fine".
            if (string.IsNullOrWhiteSpace(recallNote))
            {
                breaches.Add(new ContractBreach("recallNote",
                    "recallMethod 'none' must carry a reason, and the reason publishes. A precision figure "
                  + "beside an unexplained silence about recall is the exact asymmetry this standard exists "
                  + "to remove."));
            }
        }
        else if (recallEstimate is not (>= 0 and <= 1))
        {
            breaches.Add(new ContractBreach("recallEstimate",
                "a recall estimate is a share between 0 and 1. Name the method 'none' with a reason if you "
              + "cannot produce one."));
        }

        // ── Comparability ───────────────────────────────────────────────────────────────────────
        if (claims.Count == 0)
        {
            breaches.Add(new ContractBreach("claimClasses",
                "declare how falsifiable this tool's output is, per class (pointwise / structural / "
              + "statistical / advisory). Without it the rate penalises specificity: the more checkable "
              + "your output, the more of it can be shown wrong, and the worse you look beside a tool whose "
              + "claims are softer. The rate publishes per class and never only pooled."));
        }
        else if (claims.Sum(c => c.Judged) == 0)
        {
            breaches.Add(new ContractBreach("claimClasses",
                "the claim-class breakdown accounts for no findings at all."));
        }

        // ── The 05 pre-publication gate ─────────────────────────────────────────────────────────
        if (gitMiningVerified is not true)
        {
            breaches.Add(new ContractBreach("gitMiningVerified",
                gitMiningVerified is null
                    ? "declare whether the run had readable git history. A contained scan without a usable "
                    + ".git makes the history-derived dimensions emit false verdicts, so a run that cannot "
                    + "say whether it did is a run that did not check."
                    : "the run has declared that its git history was NOT readable. Its noise on the "
                    + "history-derived dimensions is then an environment artefact, not a capability gap, and "
                    + "publishing it would report a harness bug as a product weakness. Re-run with history "
                    + "available, or publish with those dimensions withdrawn."));
        }

        // ── Provenance ──────────────────────────────────────────────────────────────────────────
        foreach (var (field, value, why) in new[]
                 {
                     ("toolVersion", toolVersion, "which build produced these findings"),
                     ("holdoutSeed", holdoutSeed, "which published draw the run was made against"),
                     ("modelSet", modelSet, "which judges, with versions, produced the verdicts"),
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                breaches.Add(new ContractBreach(field,
                    $"provenance is required: {why}. A result that cannot be re-derived is not a result "
                  + "under this method — it is an assertion with a number in it."));
            }
        }

        // ── The anchor ──────────────────────────────────────────────────────────────────────────
        //
        // ★★ Checked HERE rather than separately, so a submitter missing the anchor AND the provenance AND
        // the claim classes is told all three at once. It was computable at /api/noise/fixrate from the
        // start, and that was the problem: a number nobody is obliged to fetch does not get fetched. The
        // noise rate has an audience and a marketing use; the fix rate has neither, so left optional the
        // published claim stays "our tool is quiet" rather than "our tool is acted upon".
        if (!hasFixRateObservations && string.IsNullOrWhiteSpace(fixRateUnavailable))
        {
            breaches.Add(new ContractBreach("fixRateObservations",
                "a publication carries the fix-rate anchor, or says why it cannot. Every other number here "
              + "rests on a judgement; this one rests on commits, and publishing only the judged half is "
              + "publishing only the half that opinion can move. Send fixRateObservations with a "
              + "fixRateWindowDays, or fixRateUnavailable with a reason — the reason publishes, so the "
              + "absence is one a reader can weigh."));
        }
        else if (hasFixRateObservations && fixRateWindowDays is not > 0)
        {
            breaches.Add(new ContractBreach("fixRateWindowDays",
                "fixRateObservations need a fixRateWindowDays — a fix rate without a period is "
              + "unfalsifiable, because over a long enough window nearly all code changes."));
        }

        // ── The ceiling that voids the run ──────────────────────────────────────────────────────
        if (VoidOnExclusions(adjudicated, excluded))
        {
            var rate = ExclusionRate(adjudicated, excluded)!.Value;

            // ★★ INVARIANT, not the box culture. This host runs da-DK and rendered "16,7 %" into a refusal
            // that a participant reads and quotes — a published figure with a comma decimal separator is a
            // figure somebody will mis-parse, and the message is part of the standard's public surface.
            var rateText = rate.ToString("P1", CultureInfo.InvariantCulture);
            var ceilingText = MaxExclusionRate.ToString("P0", CultureInfo.InvariantCulture);
            breaches.Add(new ContractBreach("excluded",
                $"VOID: {rateText} of judged findings were excluded, above the {ceilingText} ceiling. "
              + "This is not a verdict on the tool and not a pass with a caveat — it is an instrument unfit "
              + "to have been run. Exclusions are not randomly distributed: they concentrate where the "
              + "evidence is thin, which is where judging is worst, so the run is flattered by exactly the "
              + "findings it dropped. Fix the evidence the raters were shown and re-judge."));
        }

        return breaches;
    }
}
