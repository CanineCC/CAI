using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Cai.Tests;

/// <summary>
/// Boots the real Cai.Web app with ONE configured registry principal and NO partner key — the rate limiter is the
/// surface under test, so nothing here may ride the partner-key exemption. Each test simulates a distinct client IP
/// via <c>X-Forwarded-For</c> (production clears <c>KnownProxies</c>/<c>KnownIPNetworks</c>, so the forwarded chain
/// is accepted and the limiter partitions by it — exactly the dgx1-nginx-in-front topology), which both exercises
/// the real per-IP partitioning and isolates the tests' budgets from each other.
/// </summary>
public sealed class RateLimitingFixture : IDisposable
{
    public const string ProducerToken = "tok-rate-producer";

    private readonly string _root;

    public RateLimitingFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "cai-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Registry:DbPath"] = Path.Combine(_root, "registry.db"),
                ["Registry:KeysPath"] = Path.Combine(_root, "trusted-keys.json"), // absent — irrelevant to the limiter
                ["Registry:Principals:0:Token"] = ProducerToken,
                ["Registry:Principals:0:OrgId"] = "org_watchdog",
                ["Registry:Principals:0:Name"] = "watchdog.canine.dev",
                ["Registry:Principals:0:Roles:0"] = "producer",
            }));
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>A client presenting the given bearer token (or none) from the given simulated client IP.</summary>
    public HttpClient Client(string? token, string ip)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public void Dispose()
    {
        Factory.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // scratch dir cleanup is best-effort
        }
    }
}

/// <summary>
/// The API rate limiter's traffic classes — the fix for the LIVE prod 429s on <c>/api/registry/keys</c> and delivery
/// GETs: Watchdog and Assay both call from ONE LAN IP, so the open API's anonymous per-IP budget (1/s · 3/min ·
/// 15/day) throttled the delivery loop mid-flight. The contract now: a VALID registry bearer rides a generous
/// per-PRINCIPAL budget (the credential is the abuse control); the registry's two deliberately public probes
/// (<c>/keys</c>, <c>/health</c>) get their own per-IP budget generous enough that the offline-verify pattern can
/// never trip it; everything else anonymous under <c>/api</c> keeps the tight open-API budget — including requests
/// presenting an INVALID token, which also throttles token guessing.
/// </summary>
public sealed class RateLimitingTests(RateLimitingFixture fx) : IClassFixture<RateLimitingFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Authenticated_registry_burst_beyond_the_public_budget_is_never_throttled()
    {
        // 30 back-to-back authenticated reads — double the 15/day public budget, way past 1/s and 3/min. The
        // credential must lift the caller out of every per-IP window: not one 429.
        using var client = fx.Client(RateLimitingFixture.ProducerToken, "203.0.113.10");
        for (var i = 0; i < 30; i++)
        {
            var response = await client.GetAsync("/api/registry/deliveries", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Authenticated_calls_succeed_even_after_the_same_ip_exhausted_its_anonymous_budget()
    {
        // The prod topology in one test: anonymous traffic from an IP exhausts the open-API budget, then the
        // registry principal calls from the SAME IP — per-IP throttling must not bleed into the credentialed loop.
        using var anonymous = fx.Client(token: null, "203.0.113.11");
        var anonymousStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            anonymousStatuses.Add((await anonymous.GetAsync("/api/rubrics", Ct)).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, anonymousStatuses); // the budget really was exhausted

        using var authenticated = fx.Client(RateLimitingFixture.ProducerToken, "203.0.113.11");
        for (var i = 0; i < 10; i++)
        {
            var response = await authenticated.GetAsync("/api/registry/deliveries", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Anonymous_burst_on_the_open_api_is_still_limited()
    {
        // The open standard API keeps its anonymous abuse control: a rapid burst must hit 429 (1/s alone caps a
        // same-second burst at the first request per window).
        using var client = fx.Client(token: null, "203.0.113.12");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            statuses.Add((await client.GetAsync("/api/rubrics", Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, statuses[0]); // a fresh IP's first request always lands
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Tampered_token_does_not_get_the_principal_budget()
    {
        // An UNRESOLVED bearer token is anonymous traffic: it stays inside the tight open-API budget (which also
        // throttles token guessing) and never reaches the endpoint as an authenticated caller.
        using var client = fx.Client(RateLimitingFixture.ProducerToken + "-tampered", "203.0.113.13");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            statuses.Add((await client.GetAsync("/api/registry/deliveries", Ct)).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses); // throttled like any anonymous burst
        Assert.DoesNotContain(HttpStatusCode.OK, statuses); // and never authenticated
        Assert.Contains(HttpStatusCode.Unauthorized, statuses); // the un-throttled remainder is denied cleanly
    }

    [Fact]
    public async Task Anonymous_offline_verify_loop_on_keys_and_health_is_never_throttled()
    {
        // The offline-verify pattern: a consumer refetches the public key set per delivery it verifies, and
        // monitors poll health. 30 keys reads + 5 health probes back-to-back from one IP — the exact loop that
        // 429ed in production — must all land.
        using var client = fx.Client(token: null, "203.0.113.14");
        for (var i = 0; i < 30; i++)
        {
            var response = await client.GetAsync("/api/registry/keys", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/api/registry/health", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Anonymous_registry_probes_still_have_a_ceiling()
    {
        // Generous is not unlimited: the registry's public probes keep abuse protection. Push past DOUBLE the
        // per-minute budget so at least one 429 is guaranteed even if the burst straddles a window boundary.
        using var client = fx.Client(token: null, "203.0.113.15");
        var throttled = false;
        for (var i = 0; i < 601 && !throttled; i++)
        {
            throttled = (await client.GetAsync("/api/registry/keys", Ct)).StatusCode == HttpStatusCode.TooManyRequests;
        }

        Assert.True(throttled, "a 601-request anonymous burst on /api/registry/keys must hit the ceiling");
    }

    // ── The reader-facing half: the standard's own pages were the first casualty of a limit written for scrapers ──

    [Fact]
    public async Task The_self_service_checks_survive_a_burst_the_open_budget_would_kill()
    {
        // /api/score and /api/verify-delivery are what the calculator and verifier islands on codeassuranceindex.info call
        // from the reader's browser. Under the open-API budget the eighth paste of the day 429ed — for everyone
        // sharing the office's IP — so the one check the standard invites anyone to run did not work in practice.
        using var client = fx.Client(token: null, "203.0.113.20");
        for (var i = 0; i < 25; i++)
        {
            // Deliberately malformed: this asserts the LIMITER let it through, not that the payload scored.
            var response = await client.PostAsync("/api/score", JsonContent(), Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_first_party_page_read_of_the_catalogue_is_not_throttled_as_a_scraper()
    {
        // The catalogue island fetches the version list and then a catalog on every page view. "Cache the immutable
        // catalog" is right for a program and impossible for a browser, so a browser read from a trusted origin
        // rides its own budget.
        using var client = fx.Client(token: null, "203.0.113.21");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        for (var i = 0; i < 25; i++)
        {
            var response = await client.GetAsync("/api/rubrics", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_script_reading_the_catalogue_keeps_the_tight_budget()
    {
        // The exemption is for pages, not for everyone: with no browser fetch metadata the caller is a program, and
        // programs are exactly who "cache the rubric you use" is addressed to.
        using var client = fx.Client(token: null, "203.0.113.22");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            statuses.Add((await client.GetAsync("/api/rubrics", Ct)).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    private static StringContent JsonContent() =>
        new("{\"rubricVersion\":\"\"}", System.Text.Encoding.UTF8, "application/json");
}
