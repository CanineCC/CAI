namespace Cai.Web.Noise;

/// <summary>Why a finding was put in front of a person.</summary>
/// <remarks>
/// ★★ Recorded on the QUEUE and never on what the rater sees — see <see cref="CrowdItemView"/>.
/// </remarks>
public enum CrowdReason
{
    /// <summary>The cascade could not settle it. Genuinely hard, by construction.</summary>
    Contested,

    /// <summary>
    /// The judges agreed, and this is the sample that checks whether agreeing made them right.
    /// </summary>
    SpotCheck,
}

/// <summary>A finding the cascade has finished with, and who owns the repository it came from.</summary>
public sealed record CrowdCandidate(string FindingId, CascadeState State, string OwnerId);

/// <summary>A queued item, as the QUEUE holds it — reason included.</summary>
public sealed record CrowdQueueItem(string FindingId, CrowdReason Reason, string OwnerId);

/// <summary>
/// A queued item, as a RATER sees it.
/// </summary>
/// <remarks>
/// ★★ It carries the finding and nothing else — deliberately. Told that four judges already agreed, a
/// reasonable person reads "probably fine" and rubber-stamps, and the spot-check exists precisely to
/// catch the case where all four were wrong together. Labelling the item would destroy the only evidence
/// it was built to gather, so the reason cannot travel with it. Asserted structurally, because this
/// would break by someone helpfully adding a field.
/// </remarks>
public sealed record CrowdItemView(string FindingId);

/// <summary>
/// What reaches a human rater: the contested tail, plus a sample of what the judges agreed on.
/// </summary>
/// <remarks>
/// <para>★★ Passing ONLY contested findings is efficient, and is exactly where the independence gets
/// wasted. If the judges share a blind spot they agree, the finding never escalates, and the one check
/// from outside the model family never looks at the 94% that sailed through. Ensemble agreement measures
/// consistency, not correctness.</para>
/// <para>Crowd-sourcing is what makes the sample affordable: one item per person is nothing, so hundreds
/// of auto-accepted findings can be checked a month. The constraint that forced a single reviewer down
/// to 25 or 50 simply does not apply.</para>
/// </remarks>
public static class CrowdQueue
{
    /// <summary>
    /// Build the queue: every contested finding, plus a deterministic sample of the accepted ones.
    /// </summary>
    public static IReadOnlyList<CrowdQueueItem> Build(
        IReadOnlyList<CrowdCandidate> candidates, string seed, int spotCheck)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentOutOfRangeException.ThrowIfNegative(spotCheck);

        var contested = candidates
            .Where(c => c.State == CascadeState.NeedsHuman)
            .Select(c => new CrowdQueueItem(c.FindingId, CrowdReason.Contested, c.OwnerId));

        // Sampled by the same seeded rank the holdout uses, so which findings were checked is itself
        // reproducible — "we spot-checked ten" means nothing if nobody can tell which ten.
        var sampled = candidates
            .Where(c => c.State == CascadeState.Accepted)
            .OrderBy(c => HoldoutSampler.Rank(seed, c.FindingId), StringComparer.Ordinal)
            .ThenBy(c => c.FindingId, StringComparer.Ordinal)
            .Take(spotCheck)
            .Select(c => new CrowdQueueItem(c.FindingId, CrowdReason.SpotCheck, c.OwnerId));

        // ★ INTERLEAVED, not concatenated. Blocked, the first N would all be contested and position
        // would leak exactly what the view withholds — a rater who notices the pattern learns which
        // items the machines already agreed on, which is the one thing they must not know.
        return [.. contested.Concat(sampled)
            .OrderBy(i => HoldoutSampler.Rank(seed + ":order", i.FindingId), StringComparer.Ordinal)];
    }

    /// <summary>
    /// The queue as one rater may see it.
    /// </summary>
    /// <remarks>
    /// ★ Nobody rates a finding on their own estate. "This isn't a real problem" is a very human reaction
    /// to your own code being criticised, and it would bias the rate systematically rather than randomly —
    /// which no amount of averaging removes.
    /// </remarks>
    public static IReadOnlyList<CrowdQueueItem> For(IReadOnlyList<CrowdQueueItem> queue, string raterId)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return [.. queue.Where(i => !string.Equals(i.OwnerId, raterId, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// How many independent answers one finding wants before it stops being offered.
    /// </summary>
    /// <remarks>
    /// ★ ABOVE ONE, because a single person's answer has no agreement to measure against and the crowd's
    /// value is precisely that several people rated the same thing apart. Past the target the marginal
    /// answer buys nothing and costs a question some other finding never gets.
    /// </remarks>
    public const int AnswersPerItem = 3;

    /// <summary>
    /// The next single item for a rater, or null when there is nothing left for them.
    /// </summary>
    /// <remarks>
    /// <para>★ ONE ITEM, never a list. The nine-second median came from having 500 to get through; there
    /// is no slog to race when the ask is a single question, and that is a structural fix rather than a
    /// motivational one.</para>
    /// <para>★★ AND NOT THE SAME ITEM FOR EVERYBODY. Handing out the head of the queue was found, by
    /// driving eight raters through the live endpoint, to give all eight the SAME finding: eight answers
    /// on one item, none on the other seven, including every contested one. So the choice is made
    /// breadth-first on <paramref name="load"/> — least-answered wins — and ties break on a rank derived
    /// from the RATER, so two people arriving at once are sent to different findings with no coordination
    /// between them.</para>
    /// </remarks>
    /// <param name="load">
    /// Answers already in, plus hand-outs not yet answered, per finding. Counting outstanding hand-outs
    /// is what keeps two simultaneous raters apart; abandoned ones are released by the caller's lease, or
    /// the item would be held out of circulation by someone who simply closed the tab.
    /// </param>
    public static CrowdItemView? Next(
        IReadOnlyList<CrowdQueueItem> queue,
        string raterId,
        IReadOnlyCollection<string> answered,
        IReadOnlyDictionary<string, int>? load = null,
        int answersPerItem = AnswersPerItem)
    {
        ArgumentNullException.ThrowIfNull(answered);

        var seen = answered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = For(queue, raterId)
            .Where(i => !seen.Contains(i.FindingId))
            .Where(i => Load(i.FindingId) < answersPerItem)
            .OrderBy(i => Load(i.FindingId))
            .ThenBy(i => HoldoutSampler.Rank(raterId, i.FindingId), StringComparer.Ordinal)
            .FirstOrDefault();

        // ★ Projected to the view HERE, so the reason cannot escape by accident. Returning the queue
        // item and trusting every caller to strip it is the kind of discipline that holds until the
        // first person in a hurry.
        return next is null ? null : new CrowdItemView(next.FindingId);

        int Load(string findingId) =>
            load is not null && load.TryGetValue(findingId, out var n) ? n : 0;
    }
}
