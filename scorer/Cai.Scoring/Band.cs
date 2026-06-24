namespace Cai.Scoring;

/// <summary>
/// The five CAI bands. Canonical thresholds — a score lands in exactly one band. These match the Watchdog engine's
/// published band table (RRatingBand): Exemplary ≥ 90, Strong ≥ 70, Adequate ≥ 50, Weak ≥ 25, else Critical. The bands
/// are fixed-valence (always worst→best in the same order), so the band IS a reading, independent of any peer corpus.
/// </summary>
public enum Band
{
    Critical,
    Weak,
    Adequate,
    Strong,
    Exemplary,
}

public static class Bands
{
    /// <summary>The band for a 0–100 score. The thresholds are the standard; do not vary them by rubric version
    /// (a rubric version changes how the score is computed, never how a computed score is banded).</summary>
    public static Band For(double scoreZeroToOneHundred) => scoreZeroToOneHundred switch
    {
        >= 90 => Band.Exemplary,
        >= 70 => Band.Strong,
        >= 50 => Band.Adequate,
        >= 25 => Band.Weak,
        _ => Band.Critical,
    };

    /// <summary>The published display label.</summary>
    public static string Label(this Band b) => b.ToString();
}
