using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>The publication surface over HTTP: census, actionability, and what is detectable.</summary>
public sealed class PublicationApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpStatusCode Status, JsonElement Body)> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <remarks>
    /// ★ Every run here declares why it has no fix-rate anchor. The anchor is REQUIRED with each
    /// publication — see <see cref="FixRateHeadlineApiTests"/> — and these tests are about the census,
    /// the actionability split and the detectable difference, so they declare the absence rather than
    /// carry observations irrelevant to what they check.
    /// </remarks>
    private static object Run(
        int reported = 100, int adjudicated = 80, int excluded = 5, int unrated = 15,
        int validAndActionable = 30, int validNotActionable = 20, int noise = 30, int clusters = 12) =>
        new
        {
            reported, adjudicated, excluded, unrated, validAndActionable, validNotActionable, noise, clusters,
            fixRateUnavailable = "fixture — this test is about the census and the threshold",
        };

    /// <summary>
    /// ★★ A census that does not balance is REFUSED, not published with a note. Findings that fall out of
    /// a funnel are exactly the ones a reader would want, and a rate computed on a subset nobody can see
    /// is the easiest dishonest number there is — and the easiest to produce by accident.
    /// </summary>
    [Fact]
    public async Task STAR_a_census_that_does_not_balance_is_refused()
    {
        var (status, body) = await PublishAsync(Run(reported: 100, adjudicated: 60, excluded: 5, unrated: 15));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(20, body.GetProperty("shortfall").GetInt32());
    }

    [Fact]
    public async Task A_balanced_run_publishes()
    {
        var (status, body) = await PublishAsync(Run());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0.6, body.GetProperty("actionabilityRate").GetDouble(), 3);
    }

    /// <summary>
    /// ★★ The detectable difference is published, and a two-point move at twelve repositories is reported
    /// as INDISTINGUISHABLE rather than as an improvement.
    /// </summary>
    [Fact]
    public async Task STAR_a_move_smaller_than_the_threshold_is_not_reported_as_progress()
    {
        var (_, body) = await PublishAsync(new
        {
            reported = 2000, adjudicated = 2000, excluded = 0, unrated = 0,
            validAndActionable = 900, validNotActionable = 660, noise = 440,
            clusters = 12,
            previousRate = 0.20,
            fixRateUnavailable = "fixture — this test is about the detectable difference",
        });

        Assert.True(body.GetProperty("minimumDetectableDifference").GetDouble() > 0.02);
        Assert.False(body.GetProperty("distinguishableFromPrevious").GetBoolean());
    }

    /// <summary>
    /// ★★ A run reporting one repository gets no threshold at all — any difference it shows is a fact
    /// about that repository.
    /// </summary>
    [Fact]
    public async Task STAR_one_repository_yields_no_detectable_difference()
    {
        var (_, body) = await PublishAsync(Run(clusters: 1));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("minimumDetectableDifference").ValueKind);
    }

    /// <summary>★ The assumption behind the threshold is published, not buried in the implementation.</summary>
    [Fact]
    public async Task The_clustering_assumption_travels_with_the_number()
    {
        var (_, body) = await PublishAsync(Run());

        Assert.Equal(0.05, body.GetProperty("intraClusterCorrelation").GetDouble(), 4);
        Assert.Contains("cluster", body.GetProperty("mddNote").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ★★ There is no combined "unhelpful findings" figure anywhere in the response. Valid-but-not-
    /// actionable is a real cost and a different one from noise; adding them hides which problem the tool
    /// has, and a merged number is the one that would get quoted.
    /// </summary>
    [Fact]
    public async Task STAR_the_response_publishes_no_merged_noise_plus_unactionable_figure()
    {
        var (_, body) = await PublishAsync(Run());

        var raw = body.GetRawText();
        foreach (var forbidden in new[] { "combined", "unhelpful", "totalNoise" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}
