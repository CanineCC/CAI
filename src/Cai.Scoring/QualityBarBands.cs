namespace Cai.Scoring;

/// <summary>
/// Maps a 0–100 score to its <see cref="Band"/> THROUGH a quality bar (D-374). Production is the baseline
/// (90/70/50/25, identical to <see cref="Bands.For(double)"/>); a per-bar base offset scaled by a per-lens-group factor
/// shifts all four band lines together. The SCORE never changes — only where the colour bands fall — so the same code
/// always scores the same and stays comparable across repos; the bar just changes how strict "green" is for the repo's
/// criticality (a prototype's "Strong" line is lower than a mission-critical service's). This is the single source of
/// truth for the cutlines; clients ship these so engine + UI band identically.
/// </summary>
internal static class QualityBarBands
{
    // Production baseline cutoffs (top-down), identical to Bands.For.
    private const double ExemplaryAt = 90.0, HealthyAt = 70.0, FairAt = 50.0, PoorAt = 25.0;

    /// <summary>Per-bar shift applied to the baseline thresholds — lenient bars subtract (so "green" is easier),
    /// stricter bars add. Production (and any unknown/absent bar) is the 0 baseline.</summary>
    public static double BaseOffset(string? barTier) => Normalise(barTier) switch
    {
        "template" or "poc" or "one-off" or "prototype" => -18.0,
        "preview" or "alpha" or "beta" => -8.0,
        "mission-critical" => 6.0,
        _ => 0.0,
    };

    /// <summary>How fully a lens group follows the bar offset.</summary>
    public static double GroupFactor(LensGroup group) => group switch
    {
        LensGroup.Foundational => 0.4,
        LensGroup.Operational => 1.0,
        LensGroup.Safety => 0.25,
        _ => 0.7,
    };

    /// <summary>The four bar-and-group-adjusted band cutoffs (Exemplary / Healthy / Fair / Poor), clamped so they stay
    /// sane and strictly ordered even at the extremes (Exemplary ≤ 98, Poor ≥ 5).</summary>
    public static (double Exemplary, double Healthy, double Fair, double Poor) Thresholds(string? barTier, LensGroup group)
    {
        var off = BaseOffset(barTier) * GroupFactor(group);
        return (
            Math.Min(98.0, ExemplaryAt + off),
            HealthyAt + off,
            FairAt + off,
            Math.Max(5.0, PoorAt + off));
    }

    /// <summary>The band for a score through the given bar + group. The bar moves the thresholds; the resulting tier is
    /// the same positional <see cref="Band"/> every rating surface uses.</summary>
    public static Band For(double scoreZeroToOneHundred, string? barTier, LensGroup group)
    {
        var (ex, he, fa, po) = Thresholds(barTier, group);
        return
            scoreZeroToOneHundred >= ex ? Band.Exemplary
            : scoreZeroToOneHundred >= he ? Band.Healthy
            : scoreZeroToOneHundred >= fa ? Band.Fair
            : scoreZeroToOneHundred >= po ? Band.Poor
            : Band.Critical;
    }

    /// <summary>The band for a lens score through the bar, picking the lens's criticality group automatically.</summary>
    public static Band ForLens(double scoreZeroToOneHundred, string? barTier, string lens) =>
        For(scoreZeroToOneHundred, barTier, LensCatalog.GroupOf(lens));

    private static string Normalise(string? tier) =>
        (tier ?? "").Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)
            .Replace("oneoff", "one-off", StringComparison.Ordinal)
            .Replace("missioncritical", "mission-critical", StringComparison.Ordinal);
}
