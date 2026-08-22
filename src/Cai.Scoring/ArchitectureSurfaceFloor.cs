namespace Cai.Scoring;

/// <summary>
/// Caps the Architecture lens by the codebase's analyzable surface. The architecture lens grades cross-project
/// structure (coupling, cycles, dependency direction, boundaries); on a repo with almost no analyzable surface — one
/// project, or a near-empty/stub solution — those dimensions are vacuously perfect (nothing to couple, no cycle
/// possible), so the lens rolls up to ~100 on a trivially-structured repo. This floor stops "true but trivial" from
/// reading as Exemplary structure, and drops the lens entirely when there is genuinely nothing to grade.
/// </summary>
internal static class ArchitectureSurfaceFloor
{
    /// <summary>The architecture-lens score capped by analyzable surface. When there is genuinely nothing to grade (no
    /// analyzable projects) the lens is dropped (returns null) so the headline excludes it rather than crediting the
    /// cap. When surface is below the bar (too few projects AND too little production LoC) the score is capped;
    /// otherwise it is returned unchanged. The thresholds come from the rubric's pinned
    /// <see cref="ScoringParameters"/>.</summary>
    public static double? Apply(double? architectureScore, int analyzableProjects, int productionLoc, ScoringParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (architectureScore is not { } score)
        {
            return architectureScore;
        }

        // Nothing to grade (empty graph) → drop the lens from the headline entirely; an empty graph yields no
        // architecture verdict, not a passing score.
        if (analyzableProjects == 0)
        {
            return null;
        }

        // A single big library (≥ the LoC bar) still has intra-project structure worth grading, and a small but
        // multi-project solution likewise — so only cap when BOTH bars are missed.
        var surface = p.ArchitectureSurface;
        var lowSurface = analyzableProjects < surface.MinProjects && productionLoc < surface.MinProductionLoc;
        return lowSurface ? Math.Min(score, surface.LowSurfaceCap) : score;
    }
}
