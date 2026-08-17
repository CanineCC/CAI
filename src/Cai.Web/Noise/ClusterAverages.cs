namespace Cai.Web.Noise;

/// <summary>One cluster's judged findings — a cluster being a repository.</summary>
/// <param name="ClusterId">The repository. ★ Required: a COUNT of clusters cannot produce a macro average.</param>
/// <param name="Judged">Findings a human concluded on in this cluster.</param>
/// <param name="Noise">Of those, how many were noise.</param>
/// <param name="ClaimClass">
/// Optional. ★ A cluster may arrive as one row per claim class; it is still ONE cluster in the macro average.
/// </param>
public sealed record ClusterTally(string ClusterId, int Judged, int Noise, string? ClaimClass = null);

/// <summary>The same rate computed two ways, and what a single cluster was worth.</summary>
/// <param name="MicroRate">
/// Pooled: total noise over total judged. ★ A repository contributing half the findings contributes half the rate.
/// </param>
/// <param name="MacroRate">
/// Cluster-weighted: the mean of the per-cluster rates. ★ Null below two clusters — there is nothing to weight,
/// and repeating the micro figure under this name would be the most misleading option available.
/// </param>
/// <param name="LeaveOneOutLow">The lowest pooled rate obtainable by removing exactly one cluster.</param>
/// <param name="LeaveOneOutHigh">The highest. ★ Together they say how much any single repository was worth.</param>
/// <param name="MostInfluentialCluster">
/// The cluster whose removal moves the pooled rate furthest. ★ Named, because "the range is wide" without saying
/// which repository did it is a fact nobody can act on.
/// </param>
/// <param name="ClustersWithARate">
/// How many clusters had anything judged. ★ The macro average's denominator, published because it is not the
/// cluster count a reader would assume.
/// </param>
/// <param name="ClustersWithNothingJudged">
/// Clusters the run reached and judged nothing in. ★★ EXCLUDED from the macro and named: folding one in as 0 %
/// would drag the average down for free, and the more repositories went unjudged the better the tool would look.
/// </param>
public sealed record ClusterAverageSummary(
    double? MicroRate,
    double? MacroRate,
    double? LeaveOneOutLow,
    double? LeaveOneOutHigh,
    string? MostInfluentialCluster,
    int ClustersWithARate,
    IReadOnlyList<string> ClustersWithNothingJudged,
    string? Note)
{
    /// <summary>
    /// Whether the two averages differ enough to be worth reading separately.
    /// </summary>
    /// <remarks>
    /// ★★ FLAGGED RATHER THAN LEFT AS ARITHMETIC. Publishing two numbers and expecting the reader to subtract
    /// them is how the second one gets ignored. It flags a run for READING and never voids one — neither average
    /// is the wrong answer, and which one to quote depends on the question being asked.
    /// </remarks>
    public bool AveragesDiverge =>
        MicroRate is { } micro && MacroRate is { } macro
        && Math.Abs(micro - macro) >= ClusterAverages.NotableDivergence;
}

/// <summary>
/// The two averages 02 §5 requires, so no repository can dominate the number unseen.
/// </summary>
/// <remarks>
/// <para>★★ THE POOLED RATE IS A COUNT OVER A COUNT. One noisy monorepo in a draw of fourteen can move the
/// published figure several points while thirteen repositories say something else, and nothing in the number shows
/// it.</para>
///
/// <para>★★ AND THE DEFENCE CANNOT BE TO DROP THE OUTLIER — excluding a repository because its rate is extreme is
/// selecting on the outcome, which this codebase has a rule about and a measurement for (a finding cap moved
/// csharp −15.5 points). The defence is a second average that weights repositories equally, published beside the
/// first rather than instead of it.</para>
/// </remarks>
public static class ClusterAverages
{
    /// <summary>How far apart the two averages must be before the run is worth reading twice.</summary>
    /// <remarks>
    /// ★ Three points. Smaller than that is ordinary variation in how findings distribute; larger and the reader
    /// is being shown a figure whose value depends on which repositories were drawn rather than on the tool.
    /// </remarks>
    public const double NotableDivergence = 0.03;

    /// <summary>Both averages over every tally given.</summary>
    public static ClusterAverageSummary Compute(IReadOnlyCollection<ClusterTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);

        // ★★ SUMMED PER CLUSTER FIRST. A cluster arriving as one row per claim class must still be ONE cluster
        // in the macro average, or a repository reporting four classes gets four times the weight of one
        // reporting a single class — the domination the macro average exists to prevent, reintroduced by the
        // shape of the input.
        var byCluster = tallies
            .GroupBy(t => t.ClusterId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Id: g.Key, Judged: g.Sum(t => t.Judged), Noise: g.Sum(t => t.Noise)))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        if (byCluster.Count == 0)
        {
            return new ClusterAverageSummary(
                null, null, null, null, null, 0, [],
                "no per-cluster tallies were supplied, so neither average can be computed. A COUNT of clusters "
              + "is enough for the clustering interval and not enough for a cluster-weighted average.");
        }

        var totalJudged = byCluster.Sum(c => c.Judged);
        var totalNoise = byCluster.Sum(c => c.Noise);
        var micro = totalJudged > 0 ? (double?)totalNoise / totalJudged : null;

        // ★★ A cluster with nothing judged has NO rate. Folding it in as 0 % would drag the macro down for
        // free — see ClustersWithNothingJudged.
        var rated = byCluster.Where(c => c.Judged > 0).ToList();
        var empty = byCluster.Where(c => c.Judged == 0).Select(c => c.Id).ToList();

        var macro = rated.Count >= 2
            ? (double?)rated.Average(c => (double)c.Noise / c.Judged)
            : null;

        double? low = null, high = null;
        string? influential = null;

        if (byCluster.Count >= 2 && micro is { } microRate)
        {
            var withoutEach = byCluster
                .Select(c =>
                {
                    var judged = totalJudged - c.Judged;
                    var noise = totalNoise - c.Noise;
                    return (c.Id, Rate: judged > 0 ? (double?)noise / judged : null);
                })
                .Where(x => x.Rate is not null)
                .Select(x => (x.Id, Rate: x.Rate!.Value))
                .ToList();

            if (withoutEach.Count > 0)
            {
                low = withoutEach.Min(x => x.Rate);
                high = withoutEach.Max(x => x.Rate);

                // ★ Whose removal moves the number furthest, in either direction. "The range is wide" without
                // naming the repository is a fact nobody can act on.
                influential = withoutEach
                    .OrderByDescending(x => Math.Abs(x.Rate - microRate))
                    .ThenBy(x => x.Id, StringComparer.Ordinal)
                    .First().Id;
            }
        }

        var note = rated.Count switch
        {
            0 => "no cluster judged anything, so there is no rate to average.",
            1 => "only one cluster judged anything, so there is no cluster-weighted average: one repository "
               + "cannot be weighted against others, and repeating the pooled figure under the macro name "
               + "would be the most misleading option available.",
            _ => empty.Count > 0
                ? $"{empty.Count} cluster(s) judged nothing and are excluded from the macro average — counting "
                + "them as 0 % would improve the number for going unjudged."
                : null,
        };

        return new ClusterAverageSummary(
            micro, macro, low, high, influential, rated.Count, empty, note);
    }

    /// <summary>
    /// Both averages restricted to one claim class.
    /// </summary>
    /// <remarks>
    /// ★★ A pooled rate ACROSS claim classes is a category error the method already refuses — "line 42
    /// dereferences null" and "this file is a hotspot" are not falsifiable in the same sense. A pooled macro
    /// across them is the same error one level up, so the per-class figure has to be computable on its own.
    /// </remarks>
    public static ClusterAverageSummary ComputeFor(
        IReadOnlyCollection<ClusterTally> tallies, string claimClass)
    {
        ArgumentNullException.ThrowIfNull(tallies);

        return Compute([.. tallies.Where(t =>
            string.Equals(t.ClaimClass, claimClass, StringComparison.OrdinalIgnoreCase))]);
    }
}
