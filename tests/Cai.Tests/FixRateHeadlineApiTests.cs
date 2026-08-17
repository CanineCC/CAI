using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The fix rate as a HEADLINE claim rather than a side calculation.
/// </summary>
/// <remarks>
/// <para>★★ It was already computable at /api/noise/fixrate, and that is exactly the problem: a number
/// nobody is obliged to fetch is a number that will not be fetched. The noise rate has an audience and a
/// marketing use; the fix rate has neither, so left optional it stays a diagnostic and the published claim
/// remains "our tool is quiet" instead of "our tool is acted upon".</para>
/// <para>So a publication carries the anchor or says WHY it cannot — and the reason is published, which is
/// the difference between an absence a reader can weigh and one they cannot see.</para>
/// </remarks>
public sealed class FixRateHeadlineApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpStatusCode Status, JsonElement Body)> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Dictionary<string, object?> Run() => new()
    {
        // ★ The period the number measures — required since #23-2, so the rate can be tied to the
        // method version that governed it.
        ["period"] = "2026-09",
        ["reported"] = 2100,
        ["adjudicated"] = 1800,
        ["excluded"] = 60,
        ["unrated"] = 240,
        ["validAndActionable"] = 900,
        ["validNotActionable"] = 460,
        ["noise"] = 440,
        ["clusters"] = 12,
        // ★★ THE REST OF THE CONTRACT. These fields were listed at /api/noise/method as required with
        // every rate long before anything enforced them, and this fixture is what "a complete result"
        // looks like. A fixture that omits them is not a shorter test — it is a test of a publication
        // the standard would refuse.
        ["locCovered"] = 4_200_000L,
        ["recallEstimate"] = 0.62,
        ["recallMethod"] = "pooled-union",
        ["claimClasses"] = new object[]
        {
            new { claimClass = "pointwise", judged = 1200, noise = 300 },
            new { claimClass = "structural", judged = 400, noise = 100 },
            new { claimClass = "statistical", judged = 140, noise = 30 },
            new { claimClass = "advisory", judged = 60, noise = 10 },
        },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-08-a1b2c3",
        ["modelSet"] = "judge-a@2026-07, judge-b@2026-07, blind-c@2026-06, blind-d@2026-06",
        ["gitMiningVerified"] = true,
        // ★ The configuration the number was measured under — required on the PUBLICATION, not only on the
        // submission: #23-1 says deviations publish alongside the number, and this is the number.
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
    };

    private static object[] Observations() =>
    [
        new { findingId = "f1", repoId = "acme/x", outcome = "cited-location-changed", crowdVerdict = "noise" },
        new { findingId = "f2", repoId = "acme/x", outcome = "cited-location-changed", crowdVerdict = "valid-actionable" },
        new { findingId = "f3", repoId = "acme/y", outcome = "unchanged", crowdVerdict = "valid-actionable" },
        new { findingId = "f4", repoId = "acme/y", outcome = "unchanged", crowdVerdict = "noise" },
        new { findingId = "f5", repoId = "acme/z", outcome = "file-deleted", crowdVerdict = "noise" },
    ];

    /// <summary>
    /// ★★ A RATE WITH NO ANCHOR AND NO EXPLANATION IS REFUSED. Every other number in the standard rests
    /// on a judgement; this one rests on commits. Publishing the judged numbers alone is publishing only
    /// the half that opinion can move.
    /// </summary>
    [Fact]
    public async Task STAR_a_publication_with_no_fix_rate_and_no_reason_is_refused()
    {
        var (status, body) = await PublishAsync(Run());

        Assert.Equal(HttpStatusCode.BadRequest, status);
        // ★ Named as a FIELD in the breach list, not buried in prose. The anchor is checked alongside the
        // rest of the contract so a submitter missing it — and the provenance, and the claim classes — is
        // told all of it in one response rather than over six round-trips.
        var fields = body.GetProperty("breaches").EnumerateArray()
            .Select(b => b.GetProperty("field").GetString()).ToArray();
        Assert.Contains("fixRateObservations", fields);
    }

    /// <summary>
    /// ★ An absence a reader can WEIGH. A first cycle genuinely has no fix window yet, and refusing it
    /// outright would push everyone towards inventing observations — so the reason publishes instead, and
    /// the reader sees precisely which half of the claim is missing.
    /// </summary>
    [Fact]
    public async Task STAR_a_declared_reason_publishes_the_absence_rather_than_hiding_it()
    {
        var run = Run();
        run["fixRateUnavailable"] = "first cycle — the 90-day window has not elapsed";

        var (status, body) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("fixRate").GetProperty("declared").GetBoolean());
        Assert.Contains("90-day", body.GetProperty("fixRate").GetProperty("unavailableReason").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_reason_is_not_a_reason()
    {
        var run = Run();
        run["fixRateUnavailable"] = "   ";

        var (status, _) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    /// <summary>★ With observations, the anchor publishes beside the judged numbers.</summary>
    [Fact]
    public async Task The_anchor_publishes_alongside_the_noise_rate()
    {
        var run = Run();
        run["fixRateWindowDays"] = 90;
        run["fixRateObservations"] = Observations();

        var (status, body) = await PublishAsync(run);
        var fixRate = body.GetProperty("fixRate");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(fixRate.GetProperty("declared").GetBoolean());
        Assert.Equal(90, fixRate.GetProperty("windowDays").GetInt32());
        Assert.Equal(4, fixRate.GetProperty("observed").GetInt32());
        Assert.Equal(2, fixRate.GetProperty("fixedFindings").GetInt32());
        Assert.Equal(1, fixRate.GetProperty("excludedFileDeleted").GetInt32());
    }

    /// <summary>
    /// ★★ THE CONTRADICTION IS PROMOTED WITH IT. A finding the crowd called noise and the maintainer then
    /// fixed is evidence the crowd was wrong, from a source independent of every rater — buried in a side
    /// endpoint it is a curiosity; published beside the rate it is a check on the rate.
    /// </summary>
    [Fact]
    public async Task STAR_findings_called_noise_and_then_fixed_are_named_in_the_publication()
    {
        var run = Run();
        run["fixRateWindowDays"] = 90;
        run["fixRateObservations"] = Observations();

        var (_, body) = await PublishAsync(run);
        var named = body.GetProperty("fixRate").GetProperty("calledNoiseThenFixed")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(["f1"], named);
    }

    /// <summary>
    /// ★★ There is still no combined figure. The fix rate is not one minus the noise rate, and a
    /// publication offering a single "quality score" would be quoted as one within a week.
    /// </summary>
    [Fact]
    public async Task STAR_the_publication_offers_no_score_combining_the_two()
    {
        var run = Run();
        run["fixRateWindowDays"] = 90;
        run["fixRateObservations"] = Observations();

        var (_, body) = await PublishAsync(run);

        var raw = body.GetRawText();
        foreach (var forbidden in new[] { "qualityScore", "overallScore", "combined", "compositeRate" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>★ Observations with no window are refused — the window is what makes the rate falsifiable.</summary>
    [Fact]
    public async Task Observations_without_a_window_are_refused()
    {
        var run = Run();
        run["fixRateObservations"] = Observations();

        var (status, _) = await PublishAsync(run);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    /// <summary>
    /// ★ The method endpoint names the fix rate as required, so a participant reads the obligation before
    /// discovering it in a 400.
    /// </summary>
    [Fact]
    public async Task The_method_states_the_anchor_is_required_with_every_publication()
    {
        using var client = fx.Client();
        var body = await client.GetFromJsonAsync<JsonElement>("/api/noise/method", Ct);

        Assert.True(body.GetProperty("requiresFixRateAnchor").GetBoolean());
    }
}
