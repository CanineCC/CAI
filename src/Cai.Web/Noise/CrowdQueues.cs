using System.Collections.Concurrent;

namespace Cai.Web.Noise;

/// <summary>One person's answer to one finding.</summary>
/// <param name="MachineVerdict">
/// ★ What the cascade concluded, as the participant declares it. Optional, and its ABSENCE is reported
/// rather than assumed: an answer with nothing to compare against cannot be a contradiction, and counting
/// it as agreement would let a participant hide every disagreement by omitting one field.
/// </param>
/// <param name="WouldFix">
/// ★★ "Would you fix this?" — 02 §4. The taxonomy question asks a rater to apply a vocabulary they did not write;
/// this one asks what they would DO, which is a question a working engineer answers from experience in a second.
/// NULL means NOT ASKED, never "no": folding a missing answer into "no" would manufacture evidence that
/// practitioners would not act on findings nobody asked them about.
/// </param>
/// <param name="WantInReport">"Would you want this in a report?" — the other decision the tool exists to inform.</param>
public sealed record CrowdAnswer(
    string FindingId, string RaterId, NoiseVerdict Verdict, NoiseVerdict? MachineVerdict,
    bool? WouldFix = null, bool? WantInReport = null);

/// <summary>The two behavioural questions, in the words every client must use.</summary>
/// <remarks>
/// ★★ VERBATIM, PUBLISHED. Two clients asking "would you fix this?" and "is this worth fixing?" are asking
/// different questions, and the answers would not be comparable between them — nor could a reader weighing the
/// figures know what was actually asked.
/// </remarks>
public static class BehaviouralQuestions
{
    public const string WouldFix = "Would you fix this?";
    public const string WantInReport = "Would you want this in a report?";

    public const string Why =
        "02 §4: the spec is validated against what practitioners would do, rather than against their opinion of "
      + "the spec's own vocabulary. It is also the honest answer to a 9-second median review — a rater spending "
      + "nine seconds is reacting, not performing a taxonomy classification, so asking the question they are "
      + "actually answering makes the nine seconds evidence rather than a problem to explain away.";

    public const string RelationToTheRate =
        "Reported separately and NOT folded into the noise rate. They are evidence ABOUT the taxonomy, not a "
      + "second taxonomy: counting 'would not fix' as noise would silently redefine the published rate into a "
      + "mixture of the two questions that asking both exists to keep apart.";
}

/// <summary>A period's queue, who has been handed what, and what came back.</summary>
public sealed class CrowdRound
{
    public required string Period { get; init; }
    public required IReadOnlyList<CrowdQueueItem> Queue { get; init; }

    /// <summary>Findings actually handed to a rater, and when — the answer surface checks against this.</summary>
    public ConcurrentDictionary<(string Rater, string Finding), DateTimeOffset> Offered { get; } = new();

    public ConcurrentBag<CrowdAnswer> Answers { get; } = [];

    /// <summary>
    /// Findings in this queue whose answer is already settled by evidence outside the rating process.
    /// </summary>
    /// <remarks>
    /// ★ Planted INTO the ordinary queue, never alongside it. A calibration item a rater can recognise
    /// measures how carefully someone answers while being watched, which is not the quantity anyone wants.
    /// </remarks>
    public ConcurrentDictionary<string, Honeypot> Honeypots { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What raters declared about themselves — see <see cref="CrowdStratification"/>.</summary>
    public ConcurrentDictionary<string, RaterStratum> Strata { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long a hand-out holds a place before the finding returns to circulation.
    /// </summary>
    /// <remarks>
    /// ★ Without an expiry, one person opening a question and closing the tab would keep that finding out
    /// of the queue for the rest of the round — and the round would end with an unanswered item that
    /// looked, from every count, like one that had been offered.
    /// </remarks>
    public static readonly TimeSpan OfferLease = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Answers in, plus hand-outs still within their lease, per finding.
    /// </summary>
    /// <remarks>
    /// ★★ Counting outstanding hand-outs is what keeps two raters arriving at once off the same finding.
    /// Load computed from answers alone was the live defect: eight raters were handed one item because
    /// none of them had answered yet when the next was asked for.
    /// </remarks>
    public IReadOnlyDictionary<string, int> Load(DateTimeOffset now)
    {
        Dictionary<string, HashSet<string>> byFinding = [];

        foreach (var ((rater, finding), at) in Offered)
        {
            if (now - at <= OfferLease)
            {
                Add(finding, rater);
            }
        }

        // An answer counts whatever its hand-out's age — the work is done and cannot be un-done by a clock.
        foreach (var answer in Answers)
        {
            Add(answer.FindingId, answer.RaterId);
        }

        return byFinding.ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.OrdinalIgnoreCase);

        void Add(string finding, string rater)
        {
            if (!byFinding.TryGetValue(finding, out var raters))
            {
                byFinding[finding] = raters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            raters.Add(rater);
        }
    }
}

/// <summary>
/// The crowd layer's state: a queue per period, and the answers against it.
/// </summary>
/// <remarks>
/// ★ IN-MEMORY, and the same gap the submission store carries: a restart forgets which findings were
/// handed out, so an answer to an unoffered finding would start passing again. Named here rather than
/// left to be discovered, because the check it weakens is the one that keeps a participant from
/// answering the accepted pool they were deliberately not shown.
/// </remarks>
public static class CrowdQueues
{
    private static readonly ConcurrentDictionary<string, CrowdRound> Rounds = new(StringComparer.OrdinalIgnoreCase);

    public static CrowdRound Register(string period, IReadOnlyList<CrowdQueueItem> queue)
    {
        var round = new CrowdRound { Period = period, Queue = queue };
        Rounds[period] = round;
        return round;
    }

    public static CrowdRound? Find(string period) =>
        Rounds.TryGetValue(period, out var round) ? round : null;

    /// <summary>
    /// Parse a cascade state off the wire.
    /// </summary>
    /// <remarks>
    /// ★ An unrecognised state returns null so the caller can REJECT it. Defaulting it to Accepted would
    /// quietly drop candidates out of the spot-check pool, and a sample that silently shrank is
    /// indistinguishable from one that was drawn correctly.
    /// </remarks>
    public static CascadeState? ParseState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "accepted" => CascadeState.Accepted,
        "needs-round2" or "needs-round-2" or "needsround2" => CascadeState.NeedsRound2,
        "needs-human" or "needshuman" => CascadeState.NeedsHuman,
        _ => null,
    };
}
