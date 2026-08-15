using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The anchor that needs nobody's opinion: was the finding actually fixed?
/// </summary>
/// <remarks>
/// <para>★★ Every other number in this standard rests on a judgement — a judge's, a rater's, an expert's.
/// The fix rate rests on commits. If a maintainer changed the cited line after the finding was reported,
/// that is a fact, and it is the one measurement no amount of shared bias among raters can move.</para>
/// <para>★ It is NOT the complement of the noise rate and must never be presented as one. A valid finding
/// can go unfixed because nobody had time, and a worthless finding can be "fixed" by a refactor that
/// happened to touch the line. It is an ANCHOR: when the crowd and the commits disagree sharply, one of
/// them is wrong, and that is worth knowing before anything is published.</para>
/// </remarks>
public sealed class FixRateAnchorTests
{
    private static FixObservation Obs(
        string id, FixOutcome outcome, NoiseVerdict? crowd = null) =>
        new(id, "acme/thing", outcome, crowd);

    [Fact]
    public void The_rate_is_fixes_over_what_was_actually_observed()
    {
        List<FixObservation> observations =
        [
            Obs("f1", FixOutcome.CitedLocationChanged),
            Obs("f2", FixOutcome.CitedLocationChanged),
            Obs("f3", FixOutcome.Unchanged),
            Obs("f4", FixOutcome.Unchanged),
        ];

        var anchor = FixRateAnchor.Compute(observations, windowDays: 90);

        Assert.Equal(4, anchor.Observed);
        Assert.Equal(2, anchor.Fixed);
        Assert.Equal(0.5, anchor.Rate!.Value, 3);
        Assert.Equal(90, anchor.WindowDays);
    }

    /// <summary>
    /// ★★ A DELETED FILE IS NOT A FIX. Counting it as one hands a flattering fix rate to any repository
    /// mid-refactor: delete the module, and every finding in it reads as resolved. It is excluded and
    /// counted, so the exclusion is visible rather than silently improving the number.
    /// </summary>
    [Fact]
    public void STAR_a_deleted_file_is_excluded_not_counted_as_fixed()
    {
        var anchor = FixRateAnchor.Compute(
            [
                Obs("f1", FixOutcome.CitedLocationChanged),
                Obs("f2", FixOutcome.FileDeleted),
                Obs("f3", FixOutcome.Unchanged),
                Obs("f4", FixOutcome.Unchanged),
            ],
            windowDays: 90);

        // The deleted one leaves the denominator entirely: 1 of 3, not 1 of 4 and not 2 of 4.
        Assert.Equal(3, anchor.Observed);
        Assert.Equal(1, anchor.Fixed);
        Assert.Equal(1, anchor.ExcludedFileDeleted);
        Assert.Equal(1.0 / 3, anchor.Rate!.Value, 3);
    }

    /// <summary>
    /// ★ A repository that vanished is UNOBSERVED, not unfixed. Counting it as unfixed would make a
    /// disappearing repository look like a tool nobody acts on.
    /// </summary>
    [Fact]
    public void STAR_an_unobservable_repository_is_not_counted_as_unfixed()
    {
        var anchor = FixRateAnchor.Compute(
            [
                Obs("f1", FixOutcome.CitedLocationChanged),
                Obs("f2", FixOutcome.NotObservable),
                Obs("f3", FixOutcome.CitedLocationChanged),
                Obs("f4", FixOutcome.CitedLocationChanged),
            ],
            windowDays: 90);

        // 3 of 3, not 3 of 4 — the vanished repository is absent from the rate, not counted against it.
        Assert.Equal(3, anchor.Observed);
        Assert.Equal(1, anchor.Unobservable);
        Assert.Equal(1.0, anchor.Rate!.Value, 3);
    }

    /// <summary>
    /// ★★ Below the minimum the rate is NULL. Two-of-three is 67% and means nothing, and a fix rate is
    /// exactly the kind of number that gets quoted without its denominator.
    /// </summary>
    [Fact]
    public void STAR_below_the_minimum_observation_count_there_is_no_rate()
    {
        var anchor = FixRateAnchor.Compute([Obs("f1", FixOutcome.CitedLocationChanged)], windowDays: 90);

        Assert.Null(anchor.Rate);
        Assert.Equal(1, anchor.Observed);
    }

    /// <summary>
    /// ★★ THE CONTRADICTION IS THE POINT. A finding the crowd called noise, which the maintainer then
    /// fixed, is evidence the crowd was wrong — the one signal available that is independent of every
    /// rater in the pool. It is counted, and each one is a candidate honeypot for the next round.
    /// </summary>
    [Fact]
    public void STAR_a_finding_called_noise_and_then_fixed_is_counted_as_a_contradiction()
    {
        var anchor = FixRateAnchor.Compute(
            [
                Obs("f1", FixOutcome.CitedLocationChanged, crowd: NoiseVerdict.Noise),
                Obs("f2", FixOutcome.CitedLocationChanged, crowd: NoiseVerdict.ValidAndActionable),
                Obs("f3", FixOutcome.Unchanged, crowd: NoiseVerdict.Noise),
            ],
            windowDays: 90);

        Assert.Equal(["f1"], anchor.CalledNoiseThenFixed);
    }

    /// <summary>
    /// ★ A window is mandatory. "60% of findings get fixed" without a period is unfalsifiable — over a
    /// long enough window nearly all code changes, and the number converges on the churn rate.
    /// </summary>
    [Fact]
    public void A_window_of_zero_days_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FixRateAnchor.Compute([Obs("f1", FixOutcome.Unchanged)], windowDays: 0));
    }

    /// <summary>
    /// ★★ The anchor does not claim to be the complement of the noise rate, and the type will not let it
    /// be read as one: there is no "noiseRate" on it and no arithmetic between the two.
    /// </summary>
    [Fact]
    public void STAR_the_anchor_publishes_no_noise_rate_of_its_own()
    {
        var names = typeof(FixRateSummary).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Noise", StringComparison.OrdinalIgnoreCase)
                                          && !n.Contains("CalledNoise", StringComparison.Ordinal));
    }
}
