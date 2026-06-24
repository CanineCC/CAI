namespace Cai.Scoring;

/// <summary>
/// The five CAI bands — the canonical standard labels from the scoring spec (RatingScale.TierFor): Exemplary ≥ 90,
/// Healthy ≥ 70, Fair ≥ 50, Poor ≥ 25, else Critical. (The Watchdog SaaS relabels these for buyers — Strong/Adequate/
/// Weak — but the STANDARD uses the canonical names.) Fixed-valence (always worst→best in the same order), so the band
/// IS a reading, independent of any peer corpus.
/// </summary>
public enum Band
{
    Critical,
    Poor,
    Fair,
    Healthy,
    Exemplary,
}

public static class Bands
{
    /// <summary>The band for a 0–100 score. The thresholds are the standard; do not vary them by rubric version
    /// (a rubric version changes how the score is computed, never how a computed score is banded).</summary>
    public static Band For(double scoreZeroToOneHundred) => scoreZeroToOneHundred switch
    {
        >= 90 => Band.Exemplary,
        >= 70 => Band.Healthy,
        >= 50 => Band.Fair,
        >= 25 => Band.Poor,
        _ => Band.Critical,
    };

    /// <summary>The published display label.</summary>
    public static string Label(this Band b) => b.ToString();
}
