using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// What has to be published beside a rate for the rate to mean anything.
/// </summary>
/// <remarks>
/// <para>★★ The census exists because a rate computed on a subset nobody can see is the easiest dishonest
/// number in the world to produce — and the easiest to produce by accident. If what entered does not equal
/// what was judged plus what was excluded plus what nobody reached, there is a step in the pipeline that
/// is not being reported.</para>
/// <para>★★ The minimum detectable difference exists because "we improved from 22% to 20%" is, at these
/// sample sizes, usually a statement about which repositories were drawn.</para>
/// </remarks>
public sealed class PublicationSurfaceTests
{
    // ── The census has to add up ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_census_that_balances_is_accepted()
    {
        var census = PublicationSurface.CheckCensus(reported: 100, adjudicated: 80, excluded: 5, unrated: 15);

        Assert.True(census.Balances);
        Assert.Equal(0, census.Shortfall);
    }

    /// <summary>
    /// ★★ A funnel that does not add up has a step nobody is reporting, and the missing findings are
    /// exactly the ones a reader would want to see. The gap is NAMED rather than absorbed.
    /// </summary>
    [Fact]
    public void STAR_a_census_that_does_not_balance_names_the_gap()
    {
        var census = PublicationSurface.CheckCensus(reported: 100, adjudicated: 60, excluded: 5, unrated: 15);

        Assert.False(census.Balances);
        Assert.Equal(20, census.Shortfall);
    }

    [Fact]
    public void A_census_claiming_more_judged_than_entered_is_also_a_failure()
    {
        var census = PublicationSurface.CheckCensus(reported: 10, adjudicated: 12, excluded: 0, unrated: 0);

        Assert.False(census.Balances);
        Assert.Equal(-2, census.Shortfall);
    }

    // ── Actionability ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ The actionability rate is taken over VALID findings, never over all of them. Dividing by
    /// everything reported mixes precision into it, and a tool could then improve its actionability by
    /// producing more noise.
    /// </summary>
    [Fact]
    public void STAR_actionability_is_taken_over_valid_findings_only()
    {
        var rate = PublicationSurface.ActionabilityRate(
            validAndActionable: 30, validNotActionable: 20, noise: 50);

        Assert.Equal(0.6, rate!.Value, 3);
    }

    [Fact]
    public void With_no_valid_findings_there_is_no_actionability_rate()
    {
        Assert.Null(PublicationSurface.ActionabilityRate(0, 0, noise: 40));
    }

    /// <summary>
    /// ★★ "Valid but not actionable" is NOT noise, and nothing here lets the two be added together. A
    /// finding that is true and that nobody can act on is a real cost and a different one — merging them
    /// hides which problem a tool actually has.
    /// </summary>
    [Fact]
    public void STAR_the_surface_offers_no_way_to_add_unactionable_findings_to_noise()
    {
        var names = typeof(PublicationSummary).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n =>
            n.Contains("Combined", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Unhelpful", StringComparison.OrdinalIgnoreCase)
            || n.Contains("TotalNoise", StringComparison.OrdinalIgnoreCase));
    }

    // ── The minimum detectable difference ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ FINDINGS ARE NOT INDEPENDENT OBSERVATIONS. They cluster by repository: one repository with an
    /// unusual convention contributes hundreds of correlated findings, so computing power from the finding
    /// count treats 2,000 correlated observations as 2,000 independent ones and understates the detectable
    /// difference several-fold. The cluster count is required, and there is no overload without it.
    /// </summary>
    [Fact]
    public void STAR_clustering_makes_the_detectable_difference_larger_not_smaller()
    {
        var clustered = PublicationSurface.MinimumDetectableDifference(0.22, findings: 2000, clusters: 12);
        var asIfIndependent = PublicationSurface.MinimumDetectableDifference(0.22, findings: 2000, clusters: 2000);

        Assert.True(clustered > asIfIndependent,
            "clustering was ignored — 2,000 correlated findings are not 2,000 observations");
    }

    [Fact]
    public void More_repositories_make_a_smaller_difference_detectable()
    {
        var twelve = PublicationSurface.MinimumDetectableDifference(0.22, findings: 2000, clusters: 12);
        var forty = PublicationSurface.MinimumDetectableDifference(0.22, findings: 2000, clusters: 40);

        Assert.True(forty < twelve);
    }

    /// <summary>
    /// ★★ AND THE ANSWER IS USED. Two rates closer together than the detectable difference are reported
    /// as indistinguishable — not as an improvement, not as a regression, and not with a hedge in prose
    /// while the number is quoted as progress.
    /// </summary>
    [Fact]
    public void STAR_a_difference_below_the_threshold_is_not_an_improvement()
    {
        var mdd = PublicationSurface.MinimumDetectableDifference(0.22, findings: 2000, clusters: 12);

        Assert.True(mdd > 0.02, $"a 2-point move should not be detectable at 12 repositories (mdd={mdd:F3})");
        Assert.False(PublicationSurface.Distinguishable(0.22, 0.20, mdd));
        Assert.True(PublicationSurface.Distinguishable(0.22, 0.02, mdd));
    }

    [Fact]
    public void A_single_cluster_admits_no_detectable_difference_at_all()
    {
        Assert.Null(PublicationSurface.MinimumDetectableDifference(0.22, findings: 500, clusters: 1));
    }

    /// <summary>★ Everything a reader needs travels in one object, so none of it can be dropped in transit.</summary>
    [Fact]
    public void The_summary_carries_the_census_the_actionability_and_the_threshold()
    {
        var summary = PublicationSurface.Summarise(
            reported: 100, adjudicated: 80, excluded: 5, unrated: 15,
            validAndActionable: 30, validNotActionable: 20, noise: 30,
            clusters: 12);

        Assert.True(summary.Census.Balances);
        Assert.Equal(0.6, summary.ActionabilityRate!.Value, 3);
        Assert.NotNull(summary.MinimumDetectableDifference);
        Assert.Equal(12, summary.Clusters);
    }
}
