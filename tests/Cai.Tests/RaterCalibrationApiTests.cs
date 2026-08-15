using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Planting honeypots and publishing what they measured, over HTTP.
/// </summary>
/// <remarks>
/// ★ Published, because a participant's own calibration figures are worth nothing to anyone else. The
/// standard's whole claim is that a third party can check the number, and a rater pool nobody outside can
/// inspect is exactly the part a vendor would be tempted to keep quiet about.
/// </remarks>
public sealed class RaterCalibrationApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"calib-{caller}";

    private async Task RegisterQueueAsync(string period, int contested, int accepted, int spotCheck)
    {
        using var client = fx.Client();
        object[] candidates =
        [
            .. Enumerable.Range(0, contested).Select(i => new { findingId = $"c{i:D3}", state = "needs-human", ownerId = "acme" }),
            .. Enumerable.Range(0, accepted).Select(i => new { findingId = $"a{i:D3}", state = "accepted", ownerId = "acme" }),
        ];
        var response = await client.PostAsJsonAsync(
            "/api/noise/crowd/queue", new { period, seed = "s", spotCheck, candidates }, Ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PlantAsync(
        string period, string findingId, string truth = "noise",
        string source = "upstream-fix-merged", string? evidence = "https://github.com/acme/x/pull/3")
    {
        using var client = fx.Client();
        return await client.PostAsJsonAsync(
            "/api/noise/crowd/honeypots",
            new { period, honeypots = new[] { new { findingId, truth, source, evidence } } },
            Ct);
    }

    /// <summary>
    /// ★★ A honeypot justified by crowd consensus is REFUSED. Scoring raters against what the crowd
    /// agreed measures conformity: the rater who repeats the majority scores highest, and the one who
    /// spots what everyone missed scores lowest.
    /// </summary>
    [Fact]
    public async Task STAR_a_honeypot_earned_by_consensus_is_refused()
    {
        var period = Period();
        await RegisterQueueAsync(period, 2, 10, 2);

        var response = await PlantAsync(period, "c000", source: "crowd-consensus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("source", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_honeypot_without_a_link_for_evidence_is_refused()
    {
        var period = Period();
        await RegisterQueueAsync(period, 2, 10, 2);

        var response = await PlantAsync(period, "c000", evidence: "we checked");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_honeypot_on_a_finding_that_is_not_in_the_queue_is_refused()
    {
        var period = Period();
        await RegisterQueueAsync(period, 2, 10, 2);

        var response = await PlantAsync(period, "not-queued");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_earned_honeypot_is_accepted()
    {
        var period = Period();
        await RegisterQueueAsync(period, 2, 10, 2);

        var response = await PlantAsync(period, "c000", truth: "valid-actionable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// ★★ Planting a honeypot changes NOTHING a rater can see. A calibration item you can recognise
    /// measures how carefully someone answers when watched, which is not the quantity anyone wants.
    /// </summary>
    [Fact]
    public async Task STAR_a_planted_honeypot_is_indistinguishable_on_the_wire()
    {
        var period = Period();
        await RegisterQueueAsync(period, 1, 0, 0);
        await PlantAsync(period, "c000");

        using var client = fx.Client();
        var raw = await client.GetStringAsync($"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);

        Assert.Equal("{\"findingId\":\"c000\"}", raw);
    }

    /// <summary>
    /// ★ The published calibration carries the COUNT beside the figure — a reader who wants to discount
    /// four-of-five can, and a reader shown only "80%" cannot.
    /// </summary>
    [Fact]
    public async Task STAR_calibration_publishes_the_count_beside_the_accuracy()
    {
        var period = Period();
        await RegisterQueueAsync(period, 6, 0, 0);

        using var client = fx.Client();
        for (var i = 0; i < 6; i++)
        {
            await PlantAsync(period, $"c{i:D3}");
        }

        // One rater answers five honeypots; four the way the evidence says.
        for (var i = 0; i < 5; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);
            await client.PostAsJsonAsync(
                "/api/noise/crowd/answers",
                new
                {
                    period,
                    raterId = "rater-1",
                    findingId = next.GetProperty("findingId").GetString(),
                    verdict = i == 0 ? "valid-actionable" : "noise",
                },
                Ct);
        }

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/calibration/{period}", Ct);
        var rater = body.GetProperty("raters").EnumerateArray().Single(r => r.GetProperty("raterId").GetString() == "rater-1");

        Assert.Equal(5, rater.GetProperty("answered").GetInt32());
        Assert.Equal(4, rater.GetProperty("agreed").GetInt32());
        Assert.Equal(0.8, rater.GetProperty("accuracy").GetDouble(), 3);
        Assert.True(rater.GetProperty("calibrated").GetBoolean());
    }

    /// <summary>
    /// ★★ Below the minimum sample the accuracy is null, and the reason says so. A figure computed on two
    /// answers reads as a rating.
    /// </summary>
    [Fact]
    public async Task STAR_a_rater_below_the_minimum_sample_publishes_no_accuracy()
    {
        var period = Period();
        await RegisterQueueAsync(period, 3, 0, 0);
        await PlantAsync(period, "c000");

        using var client = fx.Client();
        var next = await client.GetFromJsonAsync<JsonElement>(
            $"/api/noise/crowd/next?period={period}&raterId=rater-1", Ct);
        await client.PostAsJsonAsync(
            "/api/noise/crowd/answers",
            new { period, raterId = "rater-1", findingId = next.GetProperty("findingId").GetString(), verdict = "noise" },
            Ct);

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/calibration/{period}", Ct);
        var rater = body.GetProperty("raters").EnumerateArray().Single(r => r.GetProperty("raterId").GetString() == "rater-1");

        Assert.Equal(JsonValueKind.Null, rater.GetProperty("accuracy").ValueKind);
        Assert.False(rater.GetProperty("calibrated").GetBoolean());
        Assert.Equal(5, body.GetProperty("minimumSample").GetInt32());
    }

    /// <summary>
    /// ★★ A honeypot answer never lands in the measured slices. Its answer was known before it was asked,
    /// so counting it would measure the mixture of honeypots planted rather than anything about the tool.
    /// </summary>
    [Fact]
    public async Task STAR_honeypot_answers_are_reported_apart_from_the_measurement()
    {
        var period = Period();
        await RegisterQueueAsync(period, 3, 0, 0);
        await PlantAsync(period, "c000");
        await PlantAsync(period, "c001");

        using var client = fx.Client();
        for (var i = 0; i < 3; i++)
        {
            var next = await client.GetFromJsonAsync<JsonElement>(
                $"/api/noise/crowd/next?period={period}&raterId=rater-{i}", Ct);
            await client.PostAsJsonAsync(
                "/api/noise/crowd/answers",
                new { period, raterId = $"rater-{i}", findingId = next.GetProperty("findingId").GetString(), verdict = "noise" },
                Ct);
        }

        var results = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/results/{period}", Ct);

        // ★ Asserted as a PARTITION, not as fixed counts. Which raters met a honeypot depends on the
        // dosing, and pinning the split to particular numbers would make this test a statement about the
        // dosing rather than about the separation it exists to check.
        var contested = results.GetProperty("contested").GetProperty("answered").GetInt32();
        var honeypotAnswers = results.GetProperty("honeypots").GetProperty("answered").GetInt32();

        Assert.Equal(3, contested + honeypotAnswers);
        Assert.Equal(2, results.GetProperty("honeypots").GetProperty("planted").GetInt32());

        // One of the three contested findings is not a honeypot, and only that one is measured.
        Assert.Equal(1, results.GetProperty("contested").GetProperty("queued").GetInt32());
    }
}
