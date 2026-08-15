using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// The two shared-bias controls over HTTP: who answered, and what the commits say.
/// </summary>
/// <remarks>
/// ★ Honest note on how these came about: the pure logic in <see cref="Cai.Web.Noise.CrowdStratification"/>
/// and <see cref="Cai.Web.Noise.FixRateAnchor"/> was driven test-first, but these HTTP wrappers were
/// written before their tests. They are here now because an endpoint nobody exercised is an endpoint
/// nobody has checked.
/// </remarks>
public sealed class SharedBiasApiTests(RegistryUnconfiguredFixture fx) : IClassFixture<RegistryUnconfiguredFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Period([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"bias-{caller}";

    private async Task SetUpAsync(string period, int findings)
    {
        using var client = fx.Client();
        object[] candidates =
        [
            .. Enumerable.Range(0, findings).Select(i => new { findingId = $"f{i:D3}", state = "needs-human", ownerId = "acme" }),
        ];
        (await client.PostAsJsonAsync("/api/noise/crowd/queue",
            new { period, seed = "s", spotCheck = 0, candidates }, Ct)).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> DeclareAsync(
        string period, string raterId, string affiliation, string language = "csharp")
    {
        using var client = fx.Client();
        return await client.PostAsJsonAsync(
            "/api/noise/crowd/raters", new { period, raterId, primaryLanguage = language, affiliation }, Ct);
    }

    private async Task AnswerAsync(string period, string raterId)
    {
        using var client = fx.Client();
        var next = await client.GetFromJsonAsync<JsonElement>(
            $"/api/noise/crowd/next?period={period}&raterId={raterId}", Ct);
        await client.PostAsJsonAsync(
            "/api/noise/crowd/answers",
            new { period, raterId, findingId = next.GetProperty("findingId").GetString(), verdict = "noise" },
            Ct);
    }

    // ── Who answered ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ "Unknown" cannot be declared. Undeclared is a state the store can be in — someone who never
    /// said — but not a claim anyone may make, or the conflict of interest becomes something a vendor can
    /// assert its way out of.
    /// </summary>
    [Fact]
    public async Task STAR_unknown_is_not_an_affiliation_a_rater_may_claim()
    {
        var period = Period();
        await SetUpAsync(period, 4);

        var response = await DeclareAsync(period, "r1", "unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_declared_affiliation_is_accepted()
    {
        var period = Period();
        await SetUpAsync(period, 4);

        Assert.Equal(HttpStatusCode.OK, (await DeclareAsync(period, "r1", "independent")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeclareAsync(period, "r2", "vendor-employed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeclareAsync(period, "r3", "vendor-contracted")).StatusCode);
    }

    /// <summary>
    /// ★★ The vendor's own answers are counted apart on the published results, never folded into one
    /// figure — the same rule that keeps scored and advisory dimensions from being averaged together.
    /// </summary>
    [Fact]
    public async Task STAR_results_publish_the_vendors_answers_apart_from_the_independent_ones()
    {
        var period = Period();
        await SetUpAsync(period, 8);
        await DeclareAsync(period, "indie", "independent");
        await DeclareAsync(period, "vendor", "vendor-employed");

        await AnswerAsync(period, "indie");
        await AnswerAsync(period, "vendor");
        await AnswerAsync(period, "nobody-declared-me");

        using var client = fx.Client();
        var results = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/results/{period}", Ct);
        var composition = results.GetProperty("composition");

        Assert.Equal(1, composition.GetProperty("independent").GetInt32());
        Assert.Equal(1, composition.GetProperty("vendorAffiliated").GetInt32());
        Assert.Equal(1, composition.GetProperty("undeclared").GetInt32());
    }

    /// <summary>★ A crowd speaking for one language is flagged, with its share.</summary>
    [Fact]
    public async Task STAR_a_language_dominated_crowd_is_visible_on_the_results()
    {
        var period = Period();
        await SetUpAsync(period, 12);
        for (var i = 0; i < 5; i++)
        {
            await DeclareAsync(period, $"cs-{i}", "independent", "csharp");
            await AnswerAsync(period, $"cs-{i}");
        }

        await DeclareAsync(period, "py-1", "independent", "python");
        await AnswerAsync(period, "py-1");

        using var client = fx.Client();
        var results = await client.GetFromJsonAsync<JsonElement>($"/api/noise/crowd/results/{period}", Ct);
        var composition = results.GetProperty("composition");

        Assert.True(composition.GetProperty("dominated").GetBoolean());
        Assert.Equal("csharp", composition.GetProperty("largestLanguage").GetString());
    }

    // ── What the commits say ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_fix_rate_without_a_window_is_refused()
    {
        using var client = fx.Client();
        var response = await client.PostAsJsonAsync(
            "/api/noise/fixrate",
            new { observations = new[] { new { findingId = "f1", repoId = "acme/x", outcome = "unchanged" } } },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// ★★ Deleted files leave the denominator and vanished repositories leave it too — and both counts
    /// are published, so an exclusion cannot quietly improve the rate.
    /// </summary>
    [Fact]
    public async Task STAR_the_fix_rate_publishes_both_exclusions()
    {
        using var client = fx.Client();
        var body = await client.PostAsJsonAsync(
            "/api/noise/fixrate",
            new
            {
                windowDays = 90,
                observations = new[]
                {
                    new { findingId = "f1", repoId = "acme/x", outcome = "cited-location-changed", crowdVerdict = "noise" },
                    new { findingId = "f2", repoId = "acme/x", outcome = "unchanged", crowdVerdict = (string?)null },
                    new { findingId = "f3", repoId = "acme/x", outcome = "unchanged", crowdVerdict = (string?)null },
                    new { findingId = "f4", repoId = "acme/x", outcome = "file-deleted", crowdVerdict = (string?)null },
                    new { findingId = "f5", repoId = "acme/x", outcome = "not-observable", crowdVerdict = (string?)null },
                },
            },
            Ct);

        var json = JsonDocument.Parse(await body.Content.ReadAsStringAsync(Ct)).RootElement;

        Assert.Equal(3, json.GetProperty("observed").GetInt32());
        Assert.Equal(1, json.GetProperty("fixedFindings").GetInt32());
        Assert.Equal(1, json.GetProperty("excludedFileDeleted").GetInt32());
        Assert.Equal(1, json.GetProperty("unobservable").GetInt32());
        Assert.Equal(1.0 / 3, json.GetProperty("rate").GetDouble(), 3);
    }

    /// <summary>
    /// ★★ The contradiction is published by name. A finding the crowd called noise that the maintainer
    /// then fixed is the one piece of evidence about the crowd that no rater in it produced.
    /// </summary>
    [Fact]
    public async Task STAR_findings_called_noise_and_then_fixed_are_named()
    {
        using var client = fx.Client();
        var body = await client.PostAsJsonAsync(
            "/api/noise/fixrate",
            new
            {
                windowDays = 30,
                observations = new[]
                {
                    new { findingId = "f1", repoId = "acme/x", outcome = "cited-location-changed", crowdVerdict = "noise" },
                    new { findingId = "f2", repoId = "acme/x", outcome = "cited-location-changed", crowdVerdict = "valid-actionable" },
                    new { findingId = "f3", repoId = "acme/x", outcome = "unchanged", crowdVerdict = "noise" },
                },
            },
            Ct);

        var json = JsonDocument.Parse(await body.Content.ReadAsStringAsync(Ct)).RootElement;
        var named = json.GetProperty("calledNoiseThenFixed").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(["f1"], named);
    }
}
