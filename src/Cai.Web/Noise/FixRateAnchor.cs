namespace Cai.Web.Noise;

/// <summary>What the repository's history says happened to a finding.</summary>
public enum FixOutcome
{
    /// <summary>The cited location changed after the finding was reported.</summary>
    CitedLocationChanged,

    /// <summary>The cited location is still as it was.</summary>
    Unchanged,

    /// <summary>
    /// The file is gone.
    /// </summary>
    /// <remarks>
    /// ★★ NOT A FIX. Counting deletions would hand a flattering rate to any repository mid-refactor:
    /// delete the module and every finding in it reads as resolved.
    /// </remarks>
    FileDeleted,

    /// <summary>The repository could not be read — gone, renamed, or private now.</summary>
    NotObservable,
}

/// <summary>One finding, and what the commits did to it.</summary>
/// <param name="CrowdVerdict">What the people said, when anyone did. Null when nobody rated it.</param>
public sealed record FixObservation(
    string FindingId, string RepoId, FixOutcome Outcome, NoiseVerdict? CrowdVerdict);

/// <summary>The fix rate and everything needed to read it honestly.</summary>
/// <remarks>
/// ★ Carries no noise rate and no arithmetic against one. The fix rate is not the complement of the noise
/// rate and must never be presented as one.
/// </remarks>
public sealed record FixRateSummary(
    int WindowDays, int Observed, int Fixed, double? Rate,
    int ExcludedFileDeleted, int Unobservable,
    IReadOnlyList<string> CalledNoiseThenFixed);

/// <summary>
/// The anchor that needs nobody's opinion.
/// </summary>
/// <remarks>
/// <para>★★ Every other number in the standard rests on a judgement — a judge's, a rater's, an expert's.
/// This one rests on commits: if a maintainer changed the cited line after the finding was reported, that
/// is a fact, and it is the one measurement no amount of shared bias among raters can move.</para>
/// <para>★ It is an ANCHOR, not a substitute. A valid finding goes unfixed because nobody had time; a
/// worthless one gets "fixed" by a refactor that happened to touch the line. What it buys is the
/// contradiction: when the crowd and the commits disagree sharply, one of them is wrong, and it is worth
/// knowing which before anything is published.</para>
/// </remarks>
public static class FixRateAnchor
{
    /// <summary>Below this many observations, no rate is published.</summary>
    /// <remarks>
    /// ★ Two-of-three is 67% and means nothing. A fix rate is exactly the kind of number that travels
    /// without its denominator.
    /// </remarks>
    public const int MinimumObservations = 3;

    public static FixRateSummary Compute(IReadOnlyCollection<FixObservation> observations, int windowDays)
    {
        ArgumentNullException.ThrowIfNull(observations);

        // ★ A window is mandatory. "60% of findings get fixed" without a period is unfalsifiable: over a
        // long enough window nearly all code changes and the number converges on the churn rate.
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(windowDays, 0);

        var counted = observations
            .Where(o => o.Outcome is FixOutcome.CitedLocationChanged or FixOutcome.Unchanged)
            .ToList();
        var fixedCount = counted.Count(o => o.Outcome == FixOutcome.CitedLocationChanged);

        return new FixRateSummary(
            WindowDays: windowDays,
            Observed: counted.Count,
            Fixed: fixedCount,
            Rate: counted.Count >= MinimumObservations ? (double)fixedCount / counted.Count : null,
            ExcludedFileDeleted: observations.Count(o => o.Outcome == FixOutcome.FileDeleted),

            // ★ Unobservable, never "unfixed" — a repository that vanished would otherwise look like a
            // tool nobody acts on.
            Unobservable: observations.Count(o => o.Outcome == FixOutcome.NotObservable),

            // ★★ THE CONTRADICTION IS THE POINT: a finding the crowd called noise that the maintainer
            // then fixed is evidence the crowd was wrong, from a source independent of every rater in the
            // pool. Each one is a candidate honeypot for the next round — an upstream fix is exactly the
            // earned source honeypots require.
            CalledNoiseThenFixed:
            [
                .. observations
                    .Where(o => o.Outcome == FixOutcome.CitedLocationChanged && o.CrowdVerdict?.IsNoise() == true)
                    .Select(o => o.FindingId)
                    .Order(StringComparer.Ordinal),
            ]);
    }
}
