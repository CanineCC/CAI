namespace Cai.Web.Noise;

/// <summary>
/// One item as a rater is handed it: the finding, its evidence, and nothing else.
/// </summary>
/// <param name="FindingId">The derived id, which the answer is posted back against.</param>
/// <param name="Evidence">
/// Where the finding is, at the revision the run pinned — or null when no submission ever recorded it.
/// </param>
/// <param name="EvidenceProblem">
/// ★★ Stated rather than blank. An operator can register a queue of ids no submission recorded, and serving
/// those as ordinary questions is how a round fills with answers from people who were shown nothing.
/// </param>
public sealed record CrowdOffer(string FindingId, FindingRecord? Evidence, string? EvidenceProblem);

/// <summary>
/// Handing out one crowd item — the single path, used by the API and by the public page.
/// </summary>
/// <remarks>
/// ★★ ONE PATH, DELIBERATELY. The dosing, the load-aware choice, the estate exclusion and the hand-out lease
/// are four rules that only work together; a page that re-implemented them to render its own view would be a
/// second queue that agrees with the first until the day it does not, and the disagreement would show up as an
/// agreement rate nobody could explain.
/// </remarks>
public static class CrowdOffering
{
    /// <summary>The next item for this rater, or null when there is nothing left for them.</summary>
    public static CrowdOffer? Next(CrowdRound round, string raterId, INoiseStore store, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(store);

        var answered = round.Answers.Where(a => Same(a.RaterId, raterId)).Select(a => a.FindingId).ToList();

        // ★★ Dosed, or calibration is unreachable. The live round left both raters below the minimum sample
        // because honeypots came up only by chance; among hundreds of findings a person answering one question
        // a day would never be calibrated at all.
        var honeypotsAnswered = answered.Count(round.Honeypots.ContainsKey);
        var due = HoneypotDosing.IsDue(raterId, answered.Count, honeypotsAnswered);

        // ★★ Load-aware, or the queue's head goes to everybody — which is what the live run did, handing eight
        // raters the same finding while seven others, contested ones included, went unanswered.
        if (CrowdQueue.Next(
                round.Queue, raterId, answered, round.Load(now),
                honeypots: [.. round.Honeypots.Keys], preferHoneypot: due) is not { } item)
        {
            return null;
        }

        round.Offered[(raterId, item.FindingId)] = now;

        var evidence = store.FindFinding(item.FindingId);

        return new CrowdOffer(
            item.FindingId,
            evidence,
            evidence is null
                ? "this finding cannot be shown: no submission recorded it, so there is no code to look at. "
                + "Answer nothing here — report the period and the id to whoever registered the round."
                : null);
    }

    /// <summary>
    /// Record one answer, or say why it was refused.
    /// </summary>
    /// <returns>Null when it was recorded; the refusal otherwise.</returns>
    /// <remarks>
    /// ★ THE HAND-OUT CHECK LIVES HERE, not in the caller. An answer to a finding this rater was never handed is
    /// refused — without it the queue is only a suggestion, and a participant could answer the whole accepted pool
    /// including the items they were deliberately not shown, which is what the disguise exists to prevent.
    /// </remarks>
    public static string? Record(
        CrowdRound round, string? raterId, string? findingId, NoiseVerdict verdict,
        bool? wouldFix, bool? wantInReport, NoiseVerdict? machineVerdict = null)
    {
        ArgumentNullException.ThrowIfNull(round);

        if (!round.Offered.ContainsKey((raterId ?? "", findingId ?? "")))
        {
            return "that finding was never handed to that rater";
        }

        round.Answers.Add(new CrowdAnswer(
            findingId!, raterId!, verdict, machineVerdict,

            // ★ Carried through as nullable: not asked and "no" are different answers (#13).
            wouldFix, wantInReport));

        return null;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
