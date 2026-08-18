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
    public async Task STAR_The_CROWD_Endpoints_Get_Their_Own_Budget_Not_The_15_A_Day_One()
    {
        // ★★ THE OPEN BUDGET IS SIZED FOR FETCHING AN IMMUTABLE CATALOGUE ONCE — 1/s, 3/min, 15/day. Applied to
        // the crowd it caps a rater at fifteen requests a day, which is SEVEN findings: fetch an item, post an
        // answer, fetch the next. Rollout step 2 is "open the crowd", and a limit that stops a willing rater at
        // seven items closes it again with a 429 nobody would think to look for.
        //
        // ★★ COUNTED PER ITEM, because that is the unit the budget is about: one item a minute, 120 items a day
        // — which is two requests a minute and 240 a day, since rating an item costs a GET and a POST.
        using var client = fx.Client(token: null, ip: "203.0.113.77");

        // The pair that rates one item: both inside a second, which the OPEN budget's 1/s window would refuse.
        var fetch = await client.GetAsync("/api/noise/crowd/next?period=2026-09&raterId=r1", Ct);
        var answer = await client.PostAsync("/api/noise/crowd/answers",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, fetch.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, answer.StatusCode);

        // ★ And the SECOND item inside the same minute is refused: one a minute is the rate, and a person
        // reading code and judging it does not answer twice in sixty seconds.
        var tooSoon = await client.GetAsync("/api/noise/crowd/next?period=2026-09&raterId=r1", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
    }

    [Fact]
    public async Task STAR_The_PUBLISHED_Documents_Are_Not_On_The_Fifteen_A_Day_Budget()
    {
        // ★★ THE BUDGET AND THE NO-CACHE RULE WERE INCOMPATIBLE, and the collision only shows in production.
        // Watchdog's customer-facing page renders the published rate by calling this endpoint on EVERY view and
        // caching nothing, anywhere — deliberately, because a cached rate and a suppressed rate are the same
        // event seen from outside. On the open budget that page dies after fifteen views a day from one host's
        // IP, and takes the operator console's submission budget down with it.
        //
        // ★★ These two are the documents the standard most wants read: the method is the contract a new
        // participant builds a client against, and the published result is the number the whole thing exists to
        // publish. Rate-limiting them like a scraping attempt argues against the standard's own purpose.
        using var client = fx.Client(token: null, ip: "203.0.113.91");

        // Twenty — past 1/s, past 3/min, past the 15/day open budget.
        for (var i = 0; i < 20; i++)
        {
            var method = await client.GetAsync("/api/noise/method", Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, method.StatusCode);

            var published = await client.GetAsync("/api/noise/published", Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, published.StatusCode);
        }
    }

    [Fact]
    public async Task STAR_The_Published_Read_Budget_Does_Not_Widen_The_Rest_Of_The_API()
    {
        // ★ A wider window for two documents must not become a wider window for everything: the endpoints that
        // accept a MEASUREMENT keep the budget they had.
        using var client = fx.Client(token: null, ip: "203.0.113.92");

        for (var i = 0; i < 20; i++)
        {
            await client.GetAsync("/api/noise/method", Ct);
        }

        // Same IP, an endpoint that is NOT a published document — the open budget still applies.
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 20; i++)
        {
            last = (await client.GetAsync("/api/noise/holdout/2026-09", Ct)).StatusCode;
            if (last == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public void STAR_The_Published_Read_Paths_Are_One_List_Beside_Their_Budget()
    {
        // ★★ Same reason the crowd's list lives beside its number: a budget whose paths drift from the endpoints
        // silently reverts them to the open one, and nothing fails when it does.
        Assert.Equal(60, Cai.Web.ApiRateLimiting.PublishedReadPermitsPerMinute);
        Assert.Contains("/api/noise/published", Cai.Web.ApiRateLimiting.PublishedReadPaths);
        Assert.Contains("/api/noise/method", Cai.Web.ApiRateLimiting.PublishedReadPaths);
    }

    [Fact]
    public async Task STAR_The_Crowd_Budget_Does_Not_Leak_Into_The_Rest_Of_The_API()
    {
        // ★ A separate window for one path must not widen or narrow the others: the crowd caller's minute is
        // their own, and the rubric catalogue is still on the open budget.
        using var client = fx.Client(token: null, ip: "203.0.113.78");

        await client.GetAsync("/api/noise/crowd/next?period=2026-09&raterId=r2", Ct);

        var openApi = await client.GetAsync("/api/noise/method", Ct);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, openApi.StatusCode);
    }

    [Fact]
    public void STAR_The_Crowd_Budget_Is_Stated_In_ITEMS_And_Its_Paths_Are_One_List()
    {
        // ★★ The daily ceiling cannot be exercised in a test — 240 requests over a day — so the NUMBER is
        // asserted where it is declared, beside the path list that decides who gets it. A budget whose paths
        // drift from the endpoints is a budget that quietly reverts to the open one.
        Assert.Equal(2, Cai.Web.ApiRateLimiting.CrowdPermitsPerMinute);
        Assert.Equal(240, Cai.Web.ApiRateLimiting.CrowdPermitsPerDay);

        Assert.Equal(120, Cai.Web.ApiRateLimiting.CrowdPermitsPerDay / 2);
        Assert.Contains("/api/noise/crowd", Cai.Web.ApiRateLimiting.CrowdPaths);
    }

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
