namespace Cai.Web.Noise;

/// <summary>
/// How recently a vendor developed against a holdout repository — declared by the vendor, published by CAI.
/// </summary>
/// <remarks>
/// ★★ "Has this tool seen this code before?" is a property of the TOOL, not of the holdout, so it cannot be
/// derived from the draw and has to be declared. The declaration is published, which is what makes it costly
/// to get wrong.
/// </remarks>
public enum RecencyStratum
{
    /// <summary>Never used by this vendor for development. The pristine anchor.</summary>
    NeverTrained,

    /// <summary>Used one cycle ago.</summary>
    TrainedOneCycleAgo,

    /// <summary>Used two or more cycles ago.</summary>
    TrainedTwoPlusCyclesAgo,
}

/// <summary>One stratum's measured rate within a run.</summary>
/// <param name="Stratum">Which stratum.</param>
/// <param name="Judged">Findings from repositories in it that reached a verdict.</param>
/// <param name="Noise">Of those, how many were noise.</param>
public sealed record RecencyTally(RecencyStratum Stratum, int Judged, int Noise)
{
    /// <summary>The noise rate within this stratum, or null over nothing.</summary>
    public double? NoiseRate => Judged == 0 ? null : (double)Noise / Judged;
}

/// <summary>
/// The overfitting number: how much better a tool looks on code it has been developed against.
/// </summary>
/// <remarks>
/// <para>★★ THE MOST INTERESTING FIGURE THE STANDARD PRODUCES, and one no vendor would publish about itself
/// unprompted. Every other number here can be improved by building a better tool; this one can only be
/// improved by building a tool that generalises. A large gap says the measured rate describes the vendor's
/// familiarity with the sample rather than the instrument's quality.</para>
///
/// <para>★★ IT NEEDS A PERMANENTLY PRISTINE SLICE. Without an endpoint the decay curve measures nothing, and
/// "one cycle of cooling off is enough" stays an assertion rather than a result. A run whose holdout contains
/// no never-trained repositories can still publish a rate — it simply cannot publish this, and must say so
/// rather than let the absence read as "no overfitting".</para>
///
/// <para>★ It is not airtight. A determined vendor can tune to a public corpus, and we would see it only as
/// an anomalous pristine-versus-recent gap. Worth stating openly rather than pretending otherwise.</para>
/// </remarks>
public static class RecencyStrata
{
    /// <summary>Parse a wire value, or null when the vocabulary does not know it.</summary>
    public static RecencyStratum? ParseOrNull(string? value) =>
        (value ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "nevertrained" or "pristine" => RecencyStratum.NeverTrained,
            "trainedonecycleago" or "onecycleago" => RecencyStratum.TrainedOneCycleAgo,
            "trainedtwopluscyclesago" or "twopluscyclesago" or "older" =>
                RecencyStratum.TrainedTwoPlusCyclesAgo,
            _ => null,
        };

    /// <summary>The wire value.</summary>
    public static string Wire(RecencyStratum value) => value switch
    {
        RecencyStratum.NeverTrained => "never-trained",
        RecencyStratum.TrainedOneCycleAgo => "trained-one-cycle-ago",
        _ => "trained-two-plus-cycles-ago",
    };

    /// <summary>What the stratum means, published beside the declaration.</summary>
    public static string Means(RecencyStratum value) => value switch
    {
        RecencyStratum.NeverTrained =>
            "Never used by this vendor for development — the pristine anchor the gap is measured against.",
        RecencyStratum.TrainedOneCycleAgo => "Used for development one cycle ago.",
        _ => "Used for development two or more cycles ago.",
    };

    /// <summary>
    /// The pristine-versus-trained gap in percentage points, or null when it cannot be computed.
    /// </summary>
    /// <remarks>
    /// ★★ Signed, and the sign matters. POSITIVE means the tool is noisier on code it has never seen, which
    /// is the overfitting direction. NEGATIVE means it is noisier on code it was developed against, which is
    /// odd enough to be worth a look rather than a boast — it usually means the trained slice is harder, not
    /// that familiarity hurt.
    /// </remarks>
    public static double? OverfittingGap(IReadOnlyCollection<RecencyTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);

        var pristine = tallies.FirstOrDefault(t => t.Stratum == RecencyStratum.NeverTrained);
        if (pristine?.NoiseRate is not { } untrained)
        {
            return null;
        }

        var trainedJudged = tallies.Where(t => t.Stratum != RecencyStratum.NeverTrained).Sum(t => t.Judged);
        if (trainedJudged == 0)
        {
            return null;
        }

        var trainedNoise = tallies.Where(t => t.Stratum != RecencyStratum.NeverTrained).Sum(t => t.Noise);
        return untrained - ((double)trainedNoise / trainedJudged);
    }

    /// <summary>
    /// Whether the run carries a pristine slice at all.
    /// </summary>
    /// <remarks>
    /// ★ Published as its own field. A run without one is not disqualified — the first period on a new
    /// holdout genuinely has none — but the absence has to be visible, because a missing gap and a gap of
    /// zero are opposite claims and look identical when one of them is a blank.
    /// </remarks>
    public static bool HasPristineSlice(IReadOnlyCollection<RecencyTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);
        return tallies.Any(t => t.Stratum == RecencyStratum.NeverTrained && t.Judged > 0);
    }

    /// <summary>
    /// What a reader should take from the gap.
    /// </summary>
    /// <remarks>
    /// ★ The threshold is stated rather than left to the reader, and it is deliberately generous: 5 points on
    /// a rate in the high teens is a fifth of the number, which is not a rounding artefact. It flags a run for
    /// reading, never voids one — the standard does not get to decide that a vendor overfitted.
    /// </remarks>
    public const double NotableGapPoints = 0.05;

    /// <summary>True when the gap is large enough to be worth a reader's attention.</summary>
    public static bool GapIsNotable(double? gap) => gap is { } g && Math.Abs(g) >= NotableGapPoints;
}
