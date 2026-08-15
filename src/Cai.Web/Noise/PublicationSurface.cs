namespace Cai.Web.Noise;

/// <summary>The funnel from what a tool reported to what a rate was computed on.</summary>
/// <param name="Shortfall">
/// ★ Positive when findings went missing, negative when more were judged than ever entered. Named rather
/// than absorbed: the findings that fall out of a funnel are exactly the ones a reader wants to see.
/// </param>
public sealed record CensusCheck(
    int Reported, int Adjudicated, int Excluded, int Unrated, int Shortfall)
{
    public bool Balances => Shortfall == 0;
}

/// <summary>Everything that has to travel with a published rate.</summary>
/// <remarks>
/// ★★ Deliberately offers no combined figure. "Valid but not actionable" is not noise: a finding that is
/// true and that nobody can act on is a real cost and a DIFFERENT one, and adding the two together hides
/// which problem the tool actually has.
/// </remarks>
public sealed record PublicationSummary(
    CensusCheck Census,
    int ValidAndActionable, int ValidNotActionable, int Noise,
    double? ActionabilityRate,
    int Clusters, double? MinimumDetectableDifference);

/// <summary>
/// The census, the actionability rate, and the difference that is actually detectable.
/// </summary>
public static class PublicationSurface
{
    /// <summary>
    /// How strongly findings within one repository resemble each other.
    /// </summary>
    /// <remarks>
    /// ★★ The number that makes cluster sampling honest. Findings are NOT independent observations: one
    /// repository with an unusual convention contributes hundreds of correlated findings, and treating
    /// them as independent understates the detectable difference several-fold. 0.05 is a conventional,
    /// conservative-ish default and is published so a reader can substitute their own.
    /// </remarks>
    public const double DefaultIntraClusterCorrelation = 0.05;

    /// <summary>Two-sided α = 0.05 and 80% power — z(0.975) + z(0.80).</summary>
    private const double ZSum = 1.959964 + 0.841621;

    public static CensusCheck CheckCensus(int reported, int adjudicated, int excluded, int unrated) =>
        new(reported, adjudicated, excluded, unrated, reported - (adjudicated + excluded + unrated));

    /// <summary>
    /// The share of VALID findings a person could act on.
    /// </summary>
    /// <remarks>
    /// ★ Over valid findings, never over everything reported. Dividing by the whole set mixes precision
    /// into it, and a tool could then improve its actionability by producing more noise.
    /// </remarks>
    public static double? ActionabilityRate(int validAndActionable, int validNotActionable, int noise)
    {
        _ = noise;
        var valid = validAndActionable + validNotActionable;
        return valid > 0 ? (double)validAndActionable / valid : null;
    }

    /// <summary>
    /// The smallest difference in rate this sample could detect, accounting for clustering.
    /// </summary>
    /// <remarks>
    /// <para>★★ THE CLUSTER COUNT IS REQUIRED — there is no overload without it. Computing power from the
    /// finding count treats 2,000 correlated findings as 2,000 independent observations, which is how "we
    /// improved from 22% to 20%" gets published as progress when it is a statement about which
    /// repositories happened to be drawn.</para>
    /// <para>Design effect 1 + (m − 1)·ICC, where m is findings per cluster; the effective sample size is
    /// the raw count divided by it.</para>
    /// </remarks>
    /// <returns>The detectable difference, or null when there is no sample to speak of.</returns>
    public static double? MinimumDetectableDifference(
        double baselineRate, int findings, int clusters, double icc = DefaultIntraClusterCorrelation)
    {
        // ★ One repository is not a sample. Any difference it shows is a fact about that repository.
        if (clusters < 2 || findings < 2)
        {
            return null;
        }

        var perCluster = (double)findings / clusters;
        var designEffect = 1 + ((perCluster - 1) * icc);
        var effectiveN = findings / designEffect;

        var p = Math.Clamp(baselineRate, 0.001, 0.999);
        return ZSum * Math.Sqrt(2 * p * (1 - p) / effectiveN);
    }

    /// <summary>
    /// Whether two rates differ by more than this sample can detect.
    /// </summary>
    /// <remarks>
    /// ★★ The answer has to be USED, not merely computed. A move smaller than the threshold is neither an
    /// improvement nor a regression, and reporting it as one — with a hedge in the prose while the number
    /// is quoted as progress — is the failure this function exists to make awkward.
    /// </remarks>
    public static bool Distinguishable(double a, double b, double? mdd) =>
        mdd is { } threshold && Math.Abs(a - b) >= threshold;

    public static PublicationSummary Summarise(
        int reported, int adjudicated, int excluded, int unrated,
        int validAndActionable, int validNotActionable, int noise,
        int clusters, double icc = DefaultIntraClusterCorrelation)
    {
        var judged = validAndActionable + validNotActionable + noise;
        var baseline = judged > 0 ? (double)noise / judged : 0;

        return new PublicationSummary(
            Census: CheckCensus(reported, adjudicated, excluded, unrated),
            ValidAndActionable: validAndActionable,
            ValidNotActionable: validNotActionable,
            Noise: noise,
            ActionabilityRate: ActionabilityRate(validAndActionable, validNotActionable, noise),
            Clusters: clusters,
            MinimumDetectableDifference: MinimumDetectableDifference(baseline, judged, clusters, icc));
    }
}
