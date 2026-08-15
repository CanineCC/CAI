using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// What reaches a human rater, and in what disguise.
/// </summary>
/// <remarks>
/// <para>★ The crowd sees BOTH the contested tail and a sample of AUTO-ACCEPTED findings. Passing only
/// contested items is efficient and is exactly where the independence gets wasted: if the judges share
/// a blind spot they agree, it never escalates, and no human outside the model family ever sees it.</para>
/// <para>Crowd-sourcing is what makes that sample affordable. One item per person is nothing, so
/// hundreds of auto-accepted findings can be checked a month — the constraint that forced a single
/// reviewer to 25 or 50 disappears.</para>
/// </remarks>
public sealed class CrowdQueueTests
{
    private static CrowdCandidate Item(string id, CascadeState state, string owner = "someone-else") =>
        new(id, state, owner);

    private static List<CrowdCandidate> Pool(int contested, int accepted)
    {
        List<CrowdCandidate> pool =
        [
            .. Enumerable.Range(0, contested).Select(i => Item($"c{i:D3}", CascadeState.NeedsHuman)),
            .. Enumerable.Range(0, accepted).Select(i => Item($"a{i:D3}", CascadeState.Accepted)),
        ];
        return pool;
    }

    // ── What gets in ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Every contested finding reaches a person — that is what the cascade escalated it for.</summary>
    [Fact]
    public void Every_contested_finding_is_queued()
    {
        var queue = CrowdQueue.Build(Pool(contested: 7, accepted: 200), seed: "s", spotCheck: 10);

        Assert.Equal(7, queue.Count(i => i.Reason == CrowdReason.Contested));
    }

    /// <summary>
    /// ★★ AND A SAMPLE OF THE AUTO-ACCEPTED. Without it, unanimity is unfalsifiable and the pipeline
    /// validates itself — the judges agree, the finding never escalates, and the one independent check
    /// available never looks at the 94% that sailed through.
    /// </summary>
    [Fact]
    public void STAR_a_sample_of_auto_accepted_findings_is_queued_too()
    {
        var queue = CrowdQueue.Build(Pool(contested: 7, accepted: 200), seed: "s", spotCheck: 10);

        Assert.Equal(10, queue.Count(i => i.Reason == CrowdReason.SpotCheck));
    }

    /// <summary>The spot-check is reproducible, like every other sample the standard draws.</summary>
    [Fact]
    public void The_spot_check_sample_is_deterministic_for_a_seed()
    {
        var a = CrowdQueue.Build(Pool(2, 200), "seed-1", 10).Select(i => i.FindingId).ToList();
        var b = CrowdQueue.Build(Pool(2, 200), "seed-1", 10).Select(i => i.FindingId).ToList();
        var c = CrowdQueue.Build(Pool(2, 200), "seed-2", 10).Select(i => i.FindingId).ToList();

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void A_pool_smaller_than_the_spot_check_takes_what_it_has()
    {
        var queue = CrowdQueue.Build(Pool(contested: 0, accepted: 3), seed: "s", spotCheck: 10);

        Assert.Equal(3, queue.Count(i => i.Reason == CrowdReason.SpotCheck));
    }

    // ── The disguise ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ A RATER CANNOT TELL A SPOT-CHECK FROM A CONTESTED ITEM. Told that four judges already agreed,
    /// a reasonable person reads "probably fine" and rubber-stamps — and the spot-check exists precisely
    /// to catch the case where all four were wrong together. Labelling the item would destroy the only
    /// evidence it was built to gather.
    /// </summary>
    [Fact]
    public void STAR_what_a_rater_is_shown_carries_no_hint_of_why_it_was_queued()
    {
        var names = typeof(CrowdItemView).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(["FindingId"], names);
        foreach (var w in new[] { "Reason", "Contested", "SpotCheck", "Verdict", "Judge", "State" })
        {
            Assert.DoesNotContain(names, n => n.Contains(w, StringComparison.Ordinal));
        }
    }

    /// <summary>The queue is shuffled, so position cannot leak what the label does not.</summary>
    [Fact]
    public void STAR_the_queue_is_interleaved_so_position_does_not_leak_the_reason()
    {
        var queue = CrowdQueue.Build(Pool(contested: 20, accepted: 200), seed: "s", spotCheck: 20);
        var reasons = queue.Select(i => i.Reason).ToList();

        // Blocked, the first twenty would all be contested. Interleaved, they are not.
        var firstTwenty = reasons.Take(20).Distinct().Count();
        Assert.True(firstTwenty > 1, "the queue is grouped by reason, which leaks it by position");
    }

    // ── Who may see it ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ NOBODY RATES A FINDING ON THEIR OWN ESTATE. "This isn't a real problem" is a very human reaction
    /// to your own code being criticised, and it would bias the rate systematically rather than randomly.
    /// </summary>
    [Fact]
    public void STAR_a_rater_never_sees_a_finding_from_their_own_estate()
    {
        List<CrowdCandidate> pool =
        [
            Item("mine-1", CascadeState.NeedsHuman, owner: "rater-42"),
            Item("theirs-1", CascadeState.NeedsHuman, owner: "someone-else"),
        ];

        var queue = CrowdQueue.Build(pool, "s", spotCheck: 0);
        var forRater = CrowdQueue.For(queue, raterId: "rater-42");

        Assert.DoesNotContain(forRater, i => i.FindingId == "mine-1");
        Assert.Contains(forRater, i => i.FindingId == "theirs-1");
    }

    /// <summary>
    /// ★ One item at a time. The nine-second median came from a 500-item list to get through; there is
    /// no slog to race when the ask is a single question.
    /// </summary>
    [Fact]
    public void STAR_a_rater_is_offered_one_item_not_a_list()
    {
        var queue = CrowdQueue.Build(Pool(contested: 50, accepted: 200), "s", spotCheck: 20);

        var next = CrowdQueue.Next(queue, raterId: "rater-1", answered: []);

        Assert.NotNull(next);
        Assert.IsType<CrowdItemView>(next);
    }

    [Fact]
    public void An_item_already_answered_is_not_offered_again()
    {
        var queue = CrowdQueue.Build(Pool(contested: 2, accepted: 0), "s", spotCheck: 0);
        var first = CrowdQueue.Next(queue, "rater-1", [])!;

        var second = CrowdQueue.Next(queue, "rater-1", [first.FindingId]);

        Assert.NotEqual(first.FindingId, second!.FindingId);
    }

    [Fact]
    public void A_rater_who_has_answered_everything_is_offered_nothing()
    {
        var queue = CrowdQueue.Build(Pool(contested: 2, accepted: 0), "s", spotCheck: 0);
        var all = queue.Select(i => i.FindingId).ToList();

        Assert.Null(CrowdQueue.Next(queue, "rater-1", all));
    }

    // ── Spreading the crowd across the queue ──────────────────────────────────────────────────────
    //
    // ★★ Found by RUNNING IT, not by any test above. Eight raters were driven through the live endpoint
    // and every one of them was handed the same finding — the queue has a head, and the head is the same
    // for everybody. Eight answers landed on one item and none on the other seven, including all three
    // contested ones. Every test above passes on that behaviour, because each uses a single rater.

    /// <summary>
    /// ★★ TWO RATERS ARE NOT HANDED THE SAME FINDING. A crowd that all answers one item is not a crowd;
    /// it is one over-measured finding and a queue nobody touched, and the contested tail — the items the
    /// cascade escalated BECAUSE they are hard — is exactly what goes unanswered.
    /// </summary>
    [Fact]
    public void STAR_two_raters_are_not_handed_the_same_finding()
    {
        var queue = CrowdQueue.Build(Pool(contested: 4, accepted: 40), "s", spotCheck: 4);

        var first = CrowdQueue.Next(queue, "rater-1", [])!;
        // The first hand-out is itself load: a second rater must not be sent after the same item.
        var load = new Dictionary<string, int> { [first.FindingId] = 1 };
        var second = CrowdQueue.Next(queue, "rater-2", [], load)!;

        Assert.NotEqual(first.FindingId, second.FindingId);
    }

    /// <summary>
    /// ★ An item with the answers it needs stops being offered. Independent answers are the point, so the
    /// target is above one — but past it the marginal answer buys nothing and costs a question that
    /// another finding never gets.
    /// </summary>
    [Fact]
    public void STAR_an_item_that_has_its_answers_is_not_offered_again()
    {
        var queue = CrowdQueue.Build(Pool(contested: 2, accepted: 0), "s", spotCheck: 0);
        var ids = queue.Select(i => i.FindingId).ToList();
        var load = new Dictionary<string, int> { [ids[0]] = CrowdQueue.AnswersPerItem };

        var next = CrowdQueue.Next(queue, "rater-9", [], load);

        Assert.Equal(ids[1], next!.FindingId);
    }

    /// <summary>
    /// ★★ The queue is covered BREADTH-FIRST: the first N raters touch N distinct findings. Depth-first
    /// would finish one item at a time, so a round that stops early — and a crowd round always stops when
    /// people stop answering — would have measured a handful of findings thoroughly and the rest not at all.
    /// </summary>
    [Fact]
    public void STAR_the_first_raters_cover_distinct_findings_rather_than_piling_onto_one()
    {
        var queue = CrowdQueue.Build(Pool(contested: 3, accepted: 40), "s", spotCheck: 5);
        Dictionary<string, int> load = [];

        List<string> handed = [];
        for (var i = 0; i < queue.Count; i++)
        {
            var next = CrowdQueue.Next(queue, $"rater-{i}", [], load);
            Assert.NotNull(next);
            handed.Add(next.FindingId);
            load[next.FindingId] = load.GetValueOrDefault(next.FindingId) + 1;
        }

        Assert.Equal(queue.Count, handed.Distinct().Count());
    }

    /// <summary>
    /// ★ When every item is equally loaded the choice is still deterministic PER RATER, so two people
    /// arriving at once are sent to different findings without any coordination between them.
    /// </summary>
    [Fact]
    public void The_choice_is_deterministic_for_a_rater()
    {
        var queue = CrowdQueue.Build(Pool(contested: 3, accepted: 40), "s", spotCheck: 5);

        Assert.Equal(
            CrowdQueue.Next(queue, "rater-7", [])!.FindingId,
            CrowdQueue.Next(queue, "rater-7", [])!.FindingId);
    }

    /// <summary>
    /// A queue whose every item is at target has nothing left to hand out, and says so rather than
    /// handing out an over-measured one.
    /// </summary>
    [Fact]
    public void A_fully_answered_queue_offers_nothing()
    {
        var queue = CrowdQueue.Build(Pool(contested: 2, accepted: 0), "s", spotCheck: 0);
        var load = queue.ToDictionary(i => i.FindingId, _ => CrowdQueue.AnswersPerItem);

        Assert.Null(CrowdQueue.Next(queue, "rater-9", [], load));
    }
}
