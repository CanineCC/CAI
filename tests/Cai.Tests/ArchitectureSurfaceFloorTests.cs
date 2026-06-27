using Cai.Scoring;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The architecture surface floor in isolation (the unit the lens fold calls): a repo with almost no analyzable
/// surface can't claim Exemplary structure, and a repo with NO analyzable projects has no architecture verdict at all.
/// The boundaries — one big library, two tiny projects, an already-low score — must each resolve the way a reader would
/// expect, so the floor never lifts a bad score nor punishes a genuinely-structured repo.
/// </summary>
public sealed class ArchitectureSurfaceFloorTests
{
    [Fact]
    public void Thin_one_project_repo_is_capped_at_the_low_surface_ceiling() =>
        Assert.Equal(
            ArchitectureSurfaceFloor.LowSurfaceCap,
            ArchitectureSurfaceFloor.Apply(100.0, analyzableProjects: 1, productionLoc: 200));

    [Fact]
    public void Ample_multi_project_large_repo_is_unchanged() =>
        Assert.Equal(100.0, ArchitectureSurfaceFloor.Apply(100.0, analyzableProjects: 5, productionLoc: 50_000));

    [Fact]
    public void Empty_graph_with_zero_analyzable_projects_drops_the_lens() =>
        Assert.Null(ArchitectureSurfaceFloor.Apply(100.0, analyzableProjects: 0, productionLoc: 0));

    [Fact]
    public void A_null_score_stays_null() =>
        Assert.Null(ArchitectureSurfaceFloor.Apply(null, analyzableProjects: 5, productionLoc: 50_000));

    [Fact]
    public void One_project_but_a_large_library_clears_the_floor_on_loc_alone() =>
        Assert.Equal(95.0, ArchitectureSurfaceFloor.Apply(95.0, analyzableProjects: 1, productionLoc: 50_000));

    [Fact]
    public void Two_small_projects_clear_the_floor_on_project_count_alone() =>
        Assert.Equal(95.0, ArchitectureSurfaceFloor.Apply(95.0, analyzableProjects: 2, productionLoc: 200));

    [Fact]
    public void An_already_low_score_is_never_lifted_by_the_cap() =>
        // The cap is a ceiling (min), never a floor — a tidy-but-trivial repo that already scored 40 stays 40.
        Assert.Equal(40.0, ArchitectureSurfaceFloor.Apply(40.0, analyzableProjects: 1, productionLoc: 100));
}
