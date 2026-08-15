namespace Cai.Web.Noise;

/// <summary>
/// Where a honeypot's answer comes from.
/// </summary>
/// <remarks>
/// ★★ Every member is a fact about the world that would still be true if nobody had ever rated anything.
/// There is deliberately no member for crowd consensus: scoring raters against what the crowd agreed
/// measures conformity and calls it accuracy, ranking a rater highest for repeating the majority and
/// lowest for catching what everyone else missed — which is the most valuable thing a crowd produces.
/// </remarks>
public enum HoneypotSource
{
    /// <summary>A fix for the finding was merged upstream. The maintainer agreed it was real.</summary>
    UpstreamFixMerged,

    /// <summary>The vendor withdrew or disabled the rule. They agreed it was not.</summary>
    VendorWithdrew,

    /// <summary>The advisory the finding rested on was retracted. The premise is gone.</summary>
    AdvisoryRetracted,
}

/// <summary>A finding whose answer is already settled, used to calibrate the people rating.</summary>
/// <param name="Evidence">
/// ★ A LINK — a merged pull request, a rule changelog, a retraction notice. Something a third party can
/// open. "We checked" is not evidence; it is the same claim the honeypot is meant to be independent of.
/// </param>
public sealed record Honeypot(string FindingId, NoiseVerdict Truth, HoneypotSource Source, string? Evidence);

/// <summary>One rater's record against the honeypots they happened to answer.</summary>
/// <param name="Accuracy">
/// ★★ NULL below the minimum sample — not zero, and not one. A figure computed on two answers reads as a
/// rating, and a rater promoted or dismissed on two answers is noise treated as signal.
/// </param>
public sealed record RaterScore(string RaterId, int Answered, int Agreed, double? Accuracy)
{
    public bool Calibrated => Accuracy is not null;
}

/// <summary>
/// Calibration: how well a rater agrees with findings that were settled outside the rating process.
/// </summary>
public static class RaterCalibration
{
    /// <summary>
    /// How many honeypots a rater must have answered before an accuracy figure is published for them.
    /// </summary>
    /// <remarks>
    /// ★ Five is not many, and it is chosen to be reachable at one question a day rather than to be
    /// statistically comfortable — which is why the count is always published beside the figure. A reader
    /// who wants to discount 4-of-5 can; a reader shown only "80%" cannot.
    /// </remarks>
    public const int MinimumSample = 5;

    /// <summary>
    /// Parse a honeypot source off the wire, or null when it is not an EARNED one.
    /// </summary>
    public static HoneypotSource? ParseSource(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        "upstream-fix-merged" => HoneypotSource.UpstreamFixMerged,
        "vendor-withdrew" => HoneypotSource.VendorWithdrew,
        "advisory-retracted" => HoneypotSource.AdvisoryRetracted,
        _ => null,
    };

    /// <summary>A honeypot is well-formed when its evidence is a link somebody else can open.</summary>
    public static bool IsWellFormed(Honeypot honeypot) =>
        !string.IsNullOrWhiteSpace(honeypot?.FindingId)
        && Uri.TryCreate(honeypot.Evidence, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Score every rater who answered anything, against the honeypots among those answers.
    /// </summary>
    /// <remarks>
    /// ★ Raters with no honeypot answers appear too, uncalibrated. Leaving them out would make the
    /// published list look like the whole crowd when it is only the part that happened to be measured.
    /// </remarks>
    public static IReadOnlyList<RaterScore> Score(
        IReadOnlyCollection<CrowdAnswer> answers, IReadOnlyCollection<Honeypot> honeypots)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(honeypots);

        var truth = honeypots
            .GroupBy(h => h.FindingId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Truth, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. answers
                .GroupBy(a => a.RaterId, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var scored = g.Where(a => truth.ContainsKey(a.FindingId)).ToList();

                    // ★ On the BINARY. "Valid but not actionable" versus "valid and actionable" is a
                    // judgement about the fix, not about whether the tool was right to fire, and a merged
                    // pull request rarely settles it either way.
                    var agreed = scored.Count(a => a.Verdict.IsNoise() == truth[a.FindingId].IsNoise());

                    return new RaterScore(
                        g.Key, scored.Count, agreed,
                        scored.Count >= MinimumSample ? (double)agreed / scored.Count : null);
                })
                .OrderBy(s => s.RaterId, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The answers that count towards the measurement — which is ALL of them.
    /// </summary>
    /// <remarks>
    /// ★★ Exists so the decision is written down rather than implied by an absence. Dropping the answers
    /// of raters who scored badly is selection on the outcome: the excluded rater is chosen using the very
    /// variable being measured, and what survives is the subset that agreed — a cleaner number that means
    /// less. The scores are published so a reader can weigh them; the answers stay in the denominator.
    /// </remarks>
    public static IReadOnlyList<CrowdAnswer> Retain(
        IReadOnlyCollection<CrowdAnswer> answers, IReadOnlyCollection<RaterScore> scores)
    {
        _ = scores;
        return [.. answers];
    }

    /// <summary>
    /// Drop honeypot answers from the measurement they calibrate.
    /// </summary>
    /// <remarks>
    /// ★ A honeypot's answer was known before it was asked, so counting it in the noise rate measures the
    /// mixture of honeypots that happened to be planted rather than anything about the tool.
    /// </remarks>
    public static IReadOnlyList<CrowdAnswer> ExcludeHoneypots(
        IReadOnlyCollection<CrowdAnswer> answers, IReadOnlyCollection<Honeypot> honeypots)
    {
        var planted = honeypots.Select(h => h.FindingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. answers.Where(a => !planted.Contains(a.FindingId))];
    }
}
