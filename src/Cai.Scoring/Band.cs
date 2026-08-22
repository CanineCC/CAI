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
    /// <summary>score &lt; 25 — the floor band; displayed as "Critical".</summary>
    Critical,

    /// <summary>25 ≤ score &lt; 50; displayed as "Weak".</summary>
    Poor,

    /// <summary>50 ≤ score &lt; 70; displayed as "Adequate".</summary>
    Fair,

    /// <summary>70 ≤ score &lt; 90; displayed as "Strong".</summary>
    Healthy,

    /// <summary>score ≥ 90 — the top band; displayed as "Exemplary".</summary>
    Exemplary,
}

/// <summary>Bands a 0–100 score and maps each positional rank token to its published display word.</summary>
public static class Bands
{
    /// <summary>The band for a 0–100 score under the DEFAULT cutlines (90/70/50/25).
    /// <para>The cutlines are a pinned scoring input, not a presentation detail: they decide the published WORD, and
    /// the quality bar already shifts them per repo (<see cref="QualityBarBands"/>). A rubric version carries them in
    /// its catalog's <c>scoring</c> block (<see cref="BandCutlines"/>), so a report replays under the lines it was
    /// read off. This overload resolves to <see cref="ScoringParameters.Default"/> — use
    /// <see cref="BandCutlines.For(double)"/> with the catalog's cutlines when scoring against a published rubric.</para></summary>
    public static Band For(double scoreZeroToOneHundred) =>
        ScoringParameters.Default.Bands.For(scoreZeroToOneHundred);

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
