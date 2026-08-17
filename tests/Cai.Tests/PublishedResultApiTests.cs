using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cai.Web.Noise;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The published result: stored by CAI, served by CAI, and never a bare percentage.
/// </summary>
/// <remarks>
/// <para>★★ THERE WAS NOTHING TO TRANSCLUDE. #23-4 decides that CAI owns the published result and
/// <c>watchdog.canine.dev</c> renders it at request time, never storing a copy — because two copies drifting is
/// this codebase's track record, and on this number a caching bug and a suppression are the same event from
/// outside. But <c>/api/noise/publication</c> was POST-only and computed on the fly, storing nothing: there was
/// no published number to render, so a Watchdog surface could only have restated a figure kennel computed
/// itself, which is the option #23-4 explicitly rejects.</para>
///
/// <para>★★ AND THERE WAS NO INTERVAL, anywhere. #23-4's second constraint is that the number never appears
/// without its interval and its period — "if the surface cannot carry the qualifiers, it does not carry the
/// number". That was unsatisfiable: nothing computed one.</para>
///
/// <para>★ Stored APPEND-ONLY per period. A corrected number is a correction, and a store that overwrote would
/// make the second publication indistinguishable from the first — on the one figure where §01 says being seen
/// to suppress ends the standard.</para>
/// </remarks>
public sealed class PublishedResultApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Publication(string period, int noise = 440) => new()
    {
        ["period"] = period,
        ["reported"] = 2000,
        ["adjudicated"] = 1900,
        ["excluded"] = 60,
        ["unrated"] = 40,
        ["validAndActionable"] = 900,
        ["validNotActionable"] = 1900 - 900 - noise,
        ["noise"] = noise,
        ["clusters"] = 14,
        ["locCovered"] = 4_200_000L,
        ["recallEstimate"] = 0.62,
        ["recallMethod"] = "pooled-union",
        ["claimClasses"] = new object[] { new { claimClass = "pointwise", judged = 1900, noise } },
        ["toolVersion"] = "watchdog-engine 2026.08.3",
        ["holdoutSeed"] = "cai-2026-09-9f2b41c7e0a85d36",
        ["modelSet"] = "judge-a@2026-07",
        ["gitMiningVerified"] = true,
        ["configuration"] = new { rulesetId = "watchdog-default-2026.08", isProductDefault = true },
        ["fixRateUnavailable"] = "fixture",
    };

    private async Task<JsonElement> PublishAsync(object payload)
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync("/api/noise/publication", payload, Ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    /// <remarks>★ Each test uses its OWN period: this class is one app over one database and xUnit does not
    /// guarantee ordering, so a shared period would make the tests depend on each other.</remarks>
    private async Task<(HttpStatusCode Status, JsonElement Body)> GetPublishedAsync(string period)
    {
        using var client = fx.Client();
        var response = await client.GetAsync($"/api/noise/published/{period}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    // ── The interval ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void STAR_The_Wilson_Interval_Is_Arithmetic_Not_A_Guess()
    {
        // ★★ 50 of 100 at 95 % is the textbook case: Wilson gives roughly 0.404 to 0.596. Asserting a known
        // pair is what stops a plausible-looking formula shipping — a normal-approximation interval would give
        // 0.402 to 0.598 here and diverge badly at the extremes, which is exactly where a noise rate lives.
        var (low, high) = PublicationSurface.WilsonInterval(50, 100);

        Assert.InRange(low, 0.403, 0.405);
        Assert.InRange(high, 0.595, 0.597);
    }

    [Fact]
    public void STAR_At_Zero_Noise_The_Interval_Does_Not_Claim_Certainty()
    {
        // ★★ Where the normal approximation breaks and Wilson earns its place: 0 of 200 is not "0 % to 0 %".
        // A tool that reported no noise in one sample has not proved it never will, and an interval collapsing
        // to a point is the most overconfident claim the standard could publish.
        var (low, high) = PublicationSurface.WilsonInterval(0, 200);

        Assert.Equal(0, low);
        Assert.True(high > 0.01, $"upper bound should admit uncertainty, was {high}");
    }

    [Fact]
    public void An_Empty_Sample_Has_No_Interval()
    {
        // ★ Null, not (0,1). "We measured nothing" and "it could be anything" are different claims, and the
        // second one reads as a measurement.
        Assert.Null(PublicationSurface.WilsonIntervalOrNull(0, 0));
    }

    [Fact]
    public async Task STAR_The_Published_Rate_Carries_Its_Interval()
    {
        var body = await PublishAsync(Publication("2026-10"));

        var interval = body.GetProperty("noiseRateInterval");
        var low = interval.GetProperty("low").GetDouble();
        var high = interval.GetProperty("high").GetDouble();
        var rate = body.GetProperty("noiseRate").GetDouble();

        Assert.True(low < rate && rate < high, $"{low} < {rate} < {high}");
        Assert.Equal("wilson-95", interval.GetProperty("method").GetString());
    }

    // ── Stored and served ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task STAR_An_Accepted_Publication_Is_Served_Back_For_Its_Period()
    {
        // ★★ The whole reason this endpoint exists: #23-4 has Watchdog render CAI's number at request time.
        // Without a GET there is nothing to render, and the surface could only restate a figure kennel
        // computed itself — the option #23-4 rejects, because that is the artefact that goes stale.
        await PublishAsync(Publication("2026-11", noise: 440));

        var (status, body) = await GetPublishedAsync("2026-11");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("2026-11", body.GetProperty("period").GetString());
        Assert.Equal(440, body.GetProperty("noise").GetInt32());
        Assert.True(body.GetProperty("noiseRate").GetDouble() > 0);
        Assert.True(body.GetProperty("noiseRateInterval").GetProperty("high").GetDouble() > 0);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("methodVersion").GetString()));
        Assert.True(body.GetProperty("publishedAt").GetDateTimeOffset() > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task STAR_A_Period_With_No_Published_Result_404s_With_A_REASON()
    {
        // ★★ Never an empty result. A zero-filled body reads as "we measured that period and found nothing",
        // which is a different and false claim from "nothing has been published for it" — the same discipline
        // the holdout endpoint already applies.
        var (status, body) = await GetPublishedAsync("2027-01");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("no result has been published",
            body.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("published").ValueKind);
    }

    [Fact]
    public async Task STAR_A_Correction_Is_Visible_AS_A_Correction()
    {
        // ★★ Append-only. Publishing a period twice serves the latest AND says how many earlier ones there
        // were — on the one figure where §01 says that being seen to suppress ends the standard, a store that
        // silently overwrote would make the second publication indistinguishable from the first.
        await PublishAsync(Publication("2026-12", noise: 440));
        await PublishAsync(Publication("2026-12", noise: 300));

        var (status, body) = await GetPublishedAsync("2026-12");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(300, body.GetProperty("noise").GetInt32());
        Assert.Equal(1, body.GetProperty("supersededCount").GetInt32());
        Assert.Equal(2, body.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task A_Refused_Publication_Is_Not_Stored()
    {
        // ★ Only an accepted result publishes. Storing a refused one would make it fetchable as though it had
        // passed the contract.
        var bad = Publication("2027-02");
        bad.Remove("locCovered");
        await PublishAsync(bad);

        var (status, _) = await GetPublishedAsync("2027-02");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
