namespace Cai.Scoring;

/// <summary>
/// The five CAI bands — fixed-valence worst→best, so the band IS a reading independent of any peer corpus. Thresholds
/// (RatingScale.TierFor): Exemplary ≥ 90, then ≥ 70, ≥ 50, ≥ 25, else Critical. The canonical DISPLAY words are
/// <b>Exemplary / Strong / Adequate / Weak / Critical</b> (unified with the Watchdog surveyor — one vocabulary across
/// the standard and the surveyor). The enum members below are the POSITIONAL rank tokens (kept stable as the internal
/// keys + CSS classes); <see cref="Bands.Label"/> maps each to its display word.
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

    /// <summary>The published display label — the canonical CAI vocabulary, unified with the Watchdog surveyor. The
    /// enum members are positional rank tokens; this maps them to the display words.</summary>
    public static string Label(this Band b) => b switch
    {
        Band.Exemplary => "Exemplary",
        Band.Healthy => "Strong",
        Band.Fair => "Adequate",
        Band.Poor => "Weak",
        _ => "Critical",
    };
}
