namespace Cai.Scoring;

/// <summary>
/// Maps a 0–100 score to its <see cref="Band"/> THROUGH a quality bar (D-374). Production is the baseline; a per-bar
/// base offset scaled by a per-lens-group factor shifts all four band lines together. The SCORE never changes — only
/// where the colour bands fall — so the same code always scores the same and stays comparable across repos; the bar
/// just changes how strict "green" is for the repo's criticality (a prototype's "Strong" line is lower than a
/// mission-critical service's).
/// <para>The baselines, offsets and group factors are NOT declared here — they come from
/// <see cref="ScoringParameters"/>, which a rubric catalog pins. This file used to restate 90/70/50/25 beside
/// <see cref="Bands"/>'s own copy, which meant editing one and not the other diverged them silently.</para>
/// </summary>
internal static class QualityBarBands
{
    /// <summary>The four bar-and-group-adjusted band cutoffs (Exemplary / Healthy / Fair / Poor), clamped so they stay
    /// sane and strictly ordered even at the extremes.</summary>
    public static (double Exemplary, double Healthy, double Fair, double Poor) Thresholds(
        string? barTier, LensGroup group, ScoringParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var off = p.QualityBar.OffsetFor(barTier) * p.QualityBar.FactorFor(group);
        return (
            Math.Min(p.QualityBar.ExemplaryCeiling, p.Bands.Exemplary + off),
            p.Bands.Healthy + off,
            p.Bands.Fair + off,
            Math.Max(p.QualityBar.PoorFloor, p.Bands.Poor + off));
    }

    /// <summary>The band for a score through the given bar + group. The bar moves the thresholds; the resulting tier is
    /// the same positional <see cref="Band"/> every rating surface uses.</summary>
    public static Band For(double scoreZeroToOneHundred, string? barTier, LensGroup group, ScoringParameters p)
    {
        var (ex, he, fa, po) = Thresholds(barTier, group, p);
        return
            scoreZeroToOneHundred >= ex ? Band.Exemplary
            : scoreZeroToOneHundred >= he ? Band.Healthy
            : scoreZeroToOneHundred >= fa ? Band.Fair
            : scoreZeroToOneHundred >= po ? Band.Poor
            : Band.Critical;
    }

    /// <summary>The band for a lens score through the bar, picking the lens's criticality group automatically.</summary>
    public static Band ForLens(double scoreZeroToOneHundred, string? barTier, string lens, ScoringParameters p) =>
        For(scoreZeroToOneHundred, barTier, LensCatalog.GroupOf(lens), p);
}
