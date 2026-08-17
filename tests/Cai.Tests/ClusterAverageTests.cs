using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Two averages, because one repository must not be able to dominate the number unseen.
/// </summary>
/// <remarks>
/// <para>★★ THE POOLED (MICRO) RATE IS A COUNT OVER A COUNT, so a repository contributing half the findings
/// contributes half the rate. One noisy monorepo in a draw of fourteen can move the published figure several
/// points while thirteen repositories say something else entirely, and nothing in the number shows it. 02 §5
/// requires both averages for exactly this reason.</para>
///
/// <para>★★ AND THE DEFENCE CANNOT BE TO DROP THE OUTLIER. Excluding a repository because its rate is extreme is
/// selecting on the outcome — the failure this codebase has a rule about, tested at −15.5 points on csharp. So
/// the defence has to be a second average that weights repositories equally, published beside the first.</para>
///
/// <para>★ THE LEAVE-ONE-OUT RANGE says how much any single repository could be worth. A micro rate of 23 % with
/// a range of 22–24 is a different claim from the same 23 % with a range of 14–31, and the two are
/// indistinguishable without it.</para>
/// </remarks>
public sealed class ClusterAverageTests
{
    private static ClusterTally C(string id, int judged, int noise, string? claimClass = null) =>
        new(id, judged, noise, claimClass);

    [Fact]
    public void STAR_One_Huge_Repository_Moves_The_MICRO_Rate_And_Not_The_MACRO_One()
    {
        // ★★ THE CASE THE ITEM EXISTS FOR. A monorepo with 900 findings at 50 % noise, beside nine small
        // repositories at 10 %: pooled, that is 30 % — the monorepo is half the findings, so it is half the
        // rate. Weighted by repository it is 14 %: nine repositories saying one thing and one saying another.
        // Neither number is wrong; publishing only the first hides which of the two the reader is shown.
        var tallies = new List<ClusterTally> { C("monorepo", 900, 450) };
        tallies.AddRange(Enumerable.Range(1, 9).Select(i => C($"small-{i}", 100, 10)));

        var averages = ClusterAverages.Compute(tallies);

        Assert.Equal(0.30, averages.MicroRate!.Value, 3);     // 540 / 1800
        Assert.Equal(0.14, averages.MacroRate!.Value, 3);     // (0.5 + 9×0.1) / 10
        Assert.True(averages.MacroRate < averages.MicroRate);
    }

    [Fact]
    public void STAR_The_Two_Averages_Agree_When_No_Repository_Dominates()
    {
        // ★ When every cluster is the same size the two coincide, and that agreement is itself the useful
        // signal: it says the pooled figure is not an artefact of one repository's weight.
        var averages = ClusterAverages.Compute([C("a", 100, 20), C("b", 100, 20), C("c", 100, 20)]);

        Assert.Equal(0.2, averages.MicroRate!.Value, 6);
        Assert.Equal(0.2, averages.MacroRate!.Value, 6);
        Assert.False(averages.AveragesDiverge);
    }

    [Fact]
    public void STAR_A_Divergence_Worth_Reading_Is_FLAGGED_Rather_Than_Left_To_Arithmetic()
    {
        // ★★ Publishing two numbers and expecting the reader to subtract them is how the second one gets
        // ignored. The threshold is stated, and it flags a run for READING — it never voids one, because
        // neither average is the wrong answer.
        var tallies = new List<ClusterTally> { C("monorepo", 900, 450) };
        tallies.AddRange(Enumerable.Range(1, 9).Select(i => C($"small-{i}", 100, 10)));

        var averages = ClusterAverages.Compute(tallies);

        Assert.True(averages.AveragesDiverge);
        Assert.InRange(ClusterAverages.NotableDivergence, 0.01, 0.15);
    }

    // ── The leave-one-out range ────────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_The_Range_Says_What_A_Single_Repository_Was_Worth()
    {
        var tallies = new List<ClusterTally> { C("monorepo", 900, 450) };
        tallies.AddRange(Enumerable.Range(1, 9).Select(i => C($"small-{i}", 100, 10)));

        var averages = ClusterAverages.Compute(tallies);

        // ★★ Dropping the monorepo leaves 90/900 = 10 %; dropping a small one leaves 530/1700 ≈ 31.2 %. A
        // published 30 % whose value swings between 10 and 31 on one repository is a different claim from a
        // 30 % that barely moves, and the reader cannot tell them apart without this.
        Assert.Equal(0.10, averages.LeaveOneOutLow!.Value, 3);
        Assert.Equal(0.3118, averages.LeaveOneOutHigh!.Value, 3);
        Assert.Equal("monorepo", averages.MostInfluentialCluster);
    }

    [Fact]
    public void A_Single_Cluster_Has_No_Range_And_No_Macro()
    {
        // ★★ Null, not zero and not the micro rate repeated. One repository cannot produce a cluster-weighted
        // average — there is nothing to weight — and a range computed by removing the only cluster is a rate
        // over nothing. Repeating the micro figure under the macro name would be the most misleading option.
        var averages = ClusterAverages.Compute([C("only", 100, 20)]);

        Assert.Equal(0.2, averages.MicroRate!.Value, 6);
        Assert.Null(averages.MacroRate);
        Assert.Null(averages.LeaveOneOutLow);
        Assert.Contains("one cluster", averages.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_Clusters_Means_No_Averages_At_All()
    {
        var averages = ClusterAverages.Compute([]);

        Assert.Null(averages.MicroRate);
        Assert.Null(averages.MacroRate);
        Assert.NotNull(averages.Note);
    }

    [Fact]
    public void STAR_A_Cluster_That_Judged_NOTHING_Does_Not_Count_As_Zero_Percent()
    {
        // ★★ THE DEFAULT THAT WOULD FLATTER US. A repository the run reached and judged nothing in has no
        // rate; folding it in as 0 % drags the macro average down for free, and the more repositories go
        // unjudged the better the tool looks. It is excluded from the macro and NAMED.
        var averages = ClusterAverages.Compute([C("a", 100, 40), C("b", 100, 40), C("empty", 0, 0)]);

        Assert.Equal(0.4, averages.MacroRate!.Value, 6);
        Assert.Contains("empty", averages.ClustersWithNothingJudged);
        Assert.Equal(2, averages.ClustersWithARate);
    }

    // ── Per claim class ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_The_Macro_Is_Computed_Per_CLASS_Too()
    {
        // ★★ A pooled rate across claim classes is already a category error the method refuses; a pooled
        // MACRO across them would be the same error one level up. The pointwise average must be computable
        // without the structural findings dragging it around.
        var averages = ClusterAverages.ComputeFor(
        [
            C("big", 900, 450, "pointwise"),
            C("big", 10, 1, "structural"),
            C("small", 100, 10, "pointwise"),
            C("small", 10, 9, "structural"),
        ],
            claimClass: "pointwise");

        // pointwise only: micro 460/1000 = 46 %, macro (0.5 + 0.1)/2 = 30 %
        Assert.Equal(0.46, averages.MicroRate!.Value, 3);
        Assert.Equal(0.30, averages.MacroRate!.Value, 3);
    }

    [Fact]
    public void A_Class_Nobody_Reported_Has_No_Averages_Rather_Than_Zeroes()
    {
        var averages = ClusterAverages.ComputeFor([C("a", 100, 10, "pointwise")], claimClass: "advisory");

        Assert.Null(averages.MicroRate);
        Assert.Null(averages.MacroRate);
    }

    [Fact]
    public void STAR_Tallies_For_The_Same_Cluster_Are_Summed_Not_Treated_As_Two_Clusters()
    {
        // ★★ THE SUBTLE ONE. A cluster arriving as one row per claim class must still be ONE cluster in the
        // macro average, or a repository reporting four classes gets four times the weight of one reporting a
        // single class — which is the domination the macro average exists to prevent, reintroduced by the
        // shape of the input.
        var averages = ClusterAverages.Compute(
        [
            C("a", 50, 25, "pointwise"), C("a", 50, 25, "structural"),
            C("b", 100, 10, "pointwise"),
        ]);

        Assert.Equal(2, averages.ClustersWithARate);
        Assert.Equal(0.30, averages.MacroRate!.Value, 6);   // (0.5 + 0.1) / 2
    }
}

/// <summary>
/// Both averages on the published result.
/// </summary>
/// <remarks>
/// ★★ A COUNT OF CLUSTERS CANNOT PRODUCE A CLUSTER-WEIGHTED AVERAGE. The publication carried `clusters: 14` and
/// nothing else about them, which is enough for the clustering interval and structurally insufficient for 02 §5's
/// second average — so the requirement was published and unimplementable at the same time.
/// </remarks>
public sealed class ClusterAveragePublicationTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"ca-{caller}";

    private static Dictionary<string, object?> Run(string period) => new()
    {
        ["period"] = period,
        ["reported"] = 2000,
        ["adjudicated"] = 1800,
        ["excluded"] = 60,
        ["unrated"] = 140,
        ["validAndActionable"] = 900,
        ["validNotActionable"] = 360,
        ["noise"] = 540,
        ["clusters"] = 10,
        ["locCovered"] = 4_200_000L,
        ["recallEstimate"] = 0.62,
        ["recallMethod"] = "pooled-union",
        ["claimClasses"] = new object[] { new { claimClass = "pointwise", judged = 1800, noise = 540 } },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-09-9f2b41c7e0a85d36",
        ["modelSet"] = "judge-a@2026-07",
        ["gitMiningVerified"] = true,
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        ["fixRateUnavailable"] = "fixture",
        ["rejudgeUnavailable"] = "fixture",
    };

    /// <summary>The monorepo-and-nine-smalls shape, as it arrives on the wire.</summary>
    private static object[] Tallies() =>
    [
        new { clusterId = "monorepo", judged = 900, noise = 450, claimClass = "pointwise" },
        .. Enumerable.Range(1, 9).Select(i =>
            new { clusterId = $"small-{i}", judged = 100, noise = 10, claimClass = "pointwise" }),
    ];

    private async Task<(HttpStatusCode Status, JsonElement Body)> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        return (response.StatusCode,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone());
    }

    [Fact]
    public async Task STAR_Both_Averages_Publish_With_The_Range()
    {
        var run = Run(Period());
        run["clusterTallies"] = Tallies();

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        var averages = body.GetProperty("clusterAverages");

        Assert.Equal(0.30, averages.GetProperty("micro").GetDouble(), 3);
        Assert.Equal(0.14, averages.GetProperty("macro").GetDouble(), 3);
        Assert.Equal(0.10, averages.GetProperty("leaveOneOutLow").GetDouble(), 3);
        Assert.Equal(0.3118, averages.GetProperty("leaveOneOutHigh").GetDouble(), 3);
        Assert.Equal("monorepo", averages.GetProperty("mostInfluentialCluster").GetString());

        // ★★ Flagged, not left as arithmetic for the reader to do.
        Assert.True(averages.GetProperty("averagesDiverge").GetBoolean());
    }

    [Fact]
    public async Task STAR_The_MICRO_Figure_Still_Agrees_With_The_Headline_Rate()
    {
        // ★★ TWO ROUTES TO ONE NUMBER, which is how they drift. The headline `noiseRate` comes from the census
        // counts; `clusterAverages.micro` comes from the per-cluster tallies. If a submitter's tallies do not
        // add up to its census the two disagree, and a reader is shown two different rates for one run.
        var run = Run(Period());
        run["clusterTallies"] = Tallies();

        var (_, body) = await PublishAsync(run);

        Assert.Equal(
            body.GetProperty("noiseRate").GetDouble(),
            body.GetProperty("clusterAverages").GetProperty("micro").GetDouble(), 6);
    }

    [Fact]
    public async Task STAR_Tallies_That_Contradict_The_Census_Are_REFUSED()
    {
        // ★★ The other half of the same rule. Tallies summing to a different judged total than the census is
        // not a rounding difference — one of the two is wrong, and publishing both would let the reader pick.
        var run = Run(Period());
        run["clusterTallies"] = new object[]
        {
            new { clusterId = "a", judged = 5, noise = 1, claimClass = "pointwise" },
        };

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var breaches = string.Join(" ", body.GetProperty("breaches").EnumerateArray()
            .Select(b => b.GetProperty("field").GetString()));
        Assert.Contains("clusterTallies", breaches, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STAR_The_Macro_Publishes_PER_CLAIM_CLASS_As_Well()
    {
        // ★ A pooled rate across claim classes is a category error the method refuses; a pooled macro across
        // them is the same error one level up.
        var run = Run(Period());
        run["clusterTallies"] = Tallies();

        var (_, body) = await PublishAsync(run);

        var pointwise = body.GetProperty("claimClasses").EnumerateArray()
            .Single(c => c.GetProperty("claimClass").GetString() == "pointwise");

        Assert.Equal(0.14, pointwise.GetProperty("macroRate").GetDouble(), 3);
    }

    [Fact]
    public async Task No_Tallies_Publishes_With_The_Absence_Stated()
    {
        // ★ Not a refusal — a submitter without per-cluster data is not lying — but the macro average is then
        // absent and says why, rather than being a blank a reader reads as "no domination".
        var (status, body) = await PublishAsync(Run(Period()));

        Assert.Equal(HttpStatusCode.OK, status);
        var averages = body.GetProperty("clusterAverages");
        Assert.Equal(JsonValueKind.Null, averages.GetProperty("macro").ValueKind);
        Assert.Contains("COUNT of clusters", averages.GetProperty("note").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task STAR_The_Method_Says_Both_Are_Published_And_Why()
    {
        using var client = fx.Client();
        var method = JsonDocument.Parse(await client.GetStringAsync("/api/noise/method", Ct)).RootElement;

        var averages = method.GetProperty("clusterAverages");
        Assert.Equal(ClusterAverages.NotableDivergence, averages.GetProperty("notableDivergence").GetDouble());
        Assert.Contains("dominate", averages.GetProperty("why").GetString()!, StringComparison.OrdinalIgnoreCase);

        // ★★ And that dropping the outlier is NOT the remedy — that is selecting on the outcome.
        Assert.Contains("selecting on the outcome", averages.GetProperty("why").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }
}
