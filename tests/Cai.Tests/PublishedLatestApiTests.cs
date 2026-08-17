using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// "The latest published result" — the one thing a transcluding surface can ask for without guessing.
/// </summary>
/// <remarks>
/// <para>★★ A CONSUMER DOES NOT KNOW THE PERIOD. #23-4 has watchdog.canine.dev render CAI's number at request
/// time. With only <c>/published/{period}</c> that page would have to compute a period itself — "this month",
/// or "last month if this one 404s" — which is the same class of derivation the standard exists to remove, and
/// it would silently show a stale period the month a cycle slips.</para>
///
/// <para>★ Its own fixture, and the whole lifecycle in ONE test: the empty case needs a database nothing has
/// published to, so it cannot be a separate test in a class that shares one.</para>
/// </remarks>
public sealed class PublishedLatestApiTests(RegistryUnconfiguredFixture fx)
    : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Publication(string period, int noise) => new()
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

    [Fact]
    public async Task STAR_The_Latest_Is_Served_Without_The_Caller_Naming_A_Period()
    {
        using var client = fx.Client();

        // ── Nothing published: a stated 404, never an empty result ────────────────────────────────
        var empty = await client.GetAsync("/api/noise/published", Ct);
        var emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        Assert.Contains("no result has been published",
            emptyBody.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);

        // ── Two periods, published out of order ───────────────────────────────────────────────────
        // ★★ 2026-11 second but also LATEST: the answer must come from the period, not from insertion
        // order, or a correction to an older period would become "the current number".
        await client.PostAsJsonAsync("/api/noise/publication", Publication("2026-09", 440), Ct);
        await client.PostAsJsonAsync("/api/noise/publication", Publication("2026-11", 300), Ct);
        await client.PostAsJsonAsync("/api/noise/publication", Publication("2026-09", 441), Ct);

        var latest = await client.GetAsync("/api/noise/published", Ct);
        var body = JsonDocument.Parse(await latest.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal("2026-11", body.GetProperty("period").GetString());
        Assert.Equal(300, body.GetProperty("noise").GetInt32());

        // ★ The same qualifiers the period route carries — a consumer must not get a thinner body here.
        Assert.True(body.GetProperty("noiseRateInterval").GetProperty("high").GetDouble() > 0);
        Assert.True(body.GetProperty("publishedAt").GetDateTimeOffset() > DateTimeOffset.MinValue);
        Assert.Equal(0, body.GetProperty("supersededCount").GetInt32());

        // ★ And what else exists, so a reader can walk back through the history.
        Assert.Contains("2026-09",
            body.GetProperty("publishedPeriods").EnumerateArray().Select(p => p.GetString()));
    }
}
