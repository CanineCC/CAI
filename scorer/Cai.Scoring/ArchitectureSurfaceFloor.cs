namespace Cai.Scoring;

/// <summary>
/// Caps the Architecture lens by the codebase's analyzable surface. The architecture lens grades cross-project
/// structure (coupling, cycles, dependency direction, boundaries); on a repo with almost no analyzable surface — one
/// project, or a near-empty/stub solution — those dimensions are vacuously perfect (nothing to couple, no cycle
/// possible), so the lens rolls up to ~100 on a trivially-structured repo. This floor stops "true but trivial" from
/// reading as Exemplary structure, and drops the lens entirely when there is genuinely nothing to grade.
/// </summary>
public static class ArchitectureSurfaceFloor
{
    /// <summary>A repo needs at least this many production projects before cross-project architecture metrics carry
    /// real signal. Below it (with too little production LoC) the lens is CAPPED, never zeroed.</summary>
    public const int MinProjectsForFullCredit = 2;

    /// <summary>A repo needs at least this much hand-written production LoC before cross-project architecture metrics
    /// carry real signal (a single big library can clear the bar on LoC alone).</summary>
    public const int MinProductionLocForFullCredit = 1500;

    /// <summary>Cap (0–100) applied to the architecture lens when surface is below the bar — a fair-band ceiling, so a
    /// genuinely tidy tiny repo still reads "fine" but a stub solution cannot claim Exemplary structure.</summary>
    public const double LowSurfaceCap = 69.0;

    /// <summary>The architecture-lens score capped by analyzable surface. When there is genuinely nothing to grade (no
    /// analyzable projects) the lens is dropped (returns null) so the headline excludes it rather than crediting a 69.
    /// When surface is below the bar (too few projects AND too little production LoC) the score is capped at
    /// <see cref="LowSurfaceCap"/>; otherwise it is returned unchanged.</summary>
    public static double? Apply(double? architectureScore, int analyzableProjects, int productionLoc)
    {
        if (architectureScore is not { } score)
        {
            return architectureScore;
        }

        // Nothing to grade (empty graph) → drop the lens from the headline entirely; an empty graph yields no
        // architecture verdict, not a 69.
        if (analyzableProjects == 0)
        {
            return null;
        }

        // A single big library (≥ the LoC bar) still has intra-project structure worth grading, and a small but
        // multi-project solution likewise — so only cap when BOTH bars are missed.
        var lowSurface = analyzableProjects < MinProjectsForFullCredit && productionLoc < MinProductionLocForFullCredit;
        return lowSurface ? Math.Min(score, LowSurfaceCap) : score;
    }
}
