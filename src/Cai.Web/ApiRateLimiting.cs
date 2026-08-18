using System.Net;
using Cai.Web.Registry;
using Microsoft.Extensions.Options;

namespace Cai.Web;

/// <summary>The rate-limit traffic class of a request — every class carries its own abuse control.</summary>
internal enum ApiTrafficClass
{
    /// <summary>Not under <c>/api</c> (pages, <c>/health</c>, <c>/llms.txt</c>) — never rate-limited.</summary>
    NotApi,

    /// <summary>A loopback caller (the co-located surveyor) or a valid partner key — no limit.</summary>
    Trusted,

    /// <summary>A VALID registry bearer principal. The credential itself is the abuse control (mint, rotate, revoke),
    /// so these ride a generous per-PRINCIPAL budget that is a runaway-client fuse, never a per-IP budget — Watchdog
    /// and Assay both call from ONE LAN IP, and per-IP throttling took the delivery loop down mid-flight (observed
    /// live as 429s on delivery GETs).</summary>
    Principal,

    /// <summary>Anonymous traffic to the registry's two DELIBERATELY public probes (<c>/api/registry/keys</c>,
    /// <c>/api/registry/health</c>). The offline-verify pattern refetches the key set per delivery it checks and
    /// monitors poll health, so the open API's 15/day budget must never apply here; a dedicated per-IP budget stays
    /// generous enough that a full-corpus verify loop cannot trip it while a flood still hits a ceiling.</summary>
    RegistryPublic,

    /// <summary>
    /// Anonymous traffic to the CROWD endpoints (<c>/api/noise/crowd/*</c>) — the public rating loop.
    /// </summary>
    /// <remarks>
    /// ★★ THE OPEN BUDGET WOULD CLOSE THE CROWD. 1/s, 3/min and 15/day is sized for fetching an immutable
    /// catalogue once and caching it; a rater fetches an item and posts an answer, so fifteen requests is SEVEN
    /// findings before a 429 nobody would think to look for. Rollout step 2 is "open the crowd — public,
    /// cross-vendor raters", and a limit that stops a willing one at seven items closes it again.
    /// <para>★★ COUNTED PER ITEM, because that is the unit the budget is about: <b>one item a minute, 120 items
    /// a day</b> — two requests a minute and 240 a day, since rating an item costs a GET and a POST. A person
    /// reading code and judging it does not answer twice in sixty seconds, so the minute window is the abuse
    /// control and the daily ceiling is what a determined rater could reach and a script cannot pass quietly.
    /// </para>
    /// <para>★ The PAGE at <c>/noise/rate</c> is unaffected either way: it renders server-side and its form
    /// posts to the page, not to <c>/api</c>. This budget is for API-driven raters.</para>
    /// </remarks>
    Crowd,

    /// <summary>
    /// A read of a document the standard PUBLISHES — the method contract and the published result.
    /// </summary>
    /// <remarks>
    /// ★★ THE OPEN BUDGET WAS SIZED FOR FETCHING AN IMMUTABLE CATALOGUE ONCE, and applied to these two it
    /// contradicts the standard's purpose. Watchdog's customer-facing page renders the published rate on every
    /// view and caches NOTHING — on purpose, because a cached rate and a suppressed rate are indistinguishable
    /// from outside — so on 15/day that page dies after fifteen views from one host, and starves the operator
    /// console's submission on the same IP.
    /// </remarks>
    PublishedRead,

    /// <summary>Anonymous traffic to the SELF-SERVICE verification endpoints (<c>/api/score</c>, <c>/api/verify</c>,
    /// <c>/api/verify-delivery</c>). These are the public half of "don't trust the number, reproduce it" — the
    /// calculator and verifier embedded on codeassuranceindex.info call them from the reader's own browser. The open API's
    /// 15/day per-IP budget is sized for fetching an IMMUTABLE catalog once and caching it; applied here it would
    /// exhaust after a handful of pastes and take out everyone behind the same NAT, so the one check the standard
    /// invites anyone to run would fail for whole offices. Same shape as the registry probes: a dedicated per-IP
    /// budget, generous for a person working through packages, still a ceiling against a flood.</summary>
    SelfServiceVerify,

    /// <summary>A read issued by a BROWSER from one of the first-party sites (the catalogue island on
    /// codeassuranceindex.info reading <c>/api/rubrics</c> and a version's catalog). The open API's 15/day per-IP budget is
    /// advice aimed at PROGRAMMATIC consumers — "the catalog is immutable, so cache it" — and it is good advice, but
    /// a reader's browser cannot act on it: eight page views from one office exhausted the day. The standard's own
    /// pages were the first casualty of a limit written for scrapers.</summary>
    SiteReader,

    /// <summary>Everything else under <c>/api</c>: the open standard API's anonymous per-IP budget
    /// (1/s · 3/min · 15/day). A request presenting an UNRESOLVED bearer token stays HERE — which also throttles
    /// token guessing.</summary>
    Public,
}

/// <summary>
/// Classifies a request for the API rate limiter (the chained limiters are wired in the composition root; ADR-0008 +
/// registry spec §3). Config is read per-request through DI, never snapshotted at startup — the limiter runs BEFORE
/// authentication, and a live read keeps its principal check agreeing with what the auth handler will decide. The
/// classification is computed once per request (cached on <see cref="HttpContext.Items"/>) because every limiter in
/// the chain asks for it.
/// </summary>
internal static class ApiRateLimiting
{
    /// <summary>The per-principal budget for authenticated registry traffic: 600/min (10 rps sustained) — far above
    /// a full publish-verify-fetch loop over the whole corpus, far below a flood. Partitioned by principal (org/name),
    /// NOT by IP, so co-located callers never contend.</summary>
    public const int PrincipalPermitsPerMinute = 600;

    /// <summary>The per-IP budget for the registry's anonymous public probes: 300/min (5 rps sustained) — an
    /// offline-verify loop that refetches the key set for every delivery of a whole corpus stays comfortably inside;
    /// a scraper does not.</summary>
    public const int RegistryPublicPermitsPerMinute = 300;

    /// <summary>The per-IP budget for the anonymous self-service checks: 60/min (1 rps sustained). A reader working
    /// through a stack of deliveries by hand never approaches it; a scripted flood of folds still meets a ceiling.
    /// Deliberately lower than the registry probes' 300/min because a fold costs real work, where a key fetch does not.</summary>
    public const int SelfServiceVerifyPermitsPerMinute = 60;

    /// <summary>The endpoints that carry <see cref="ApiTrafficClass.SelfServiceVerify"/>. Kept as one list so the
    /// classifier and the API reference cannot drift apart about which checks are open to the public.</summary>
    public static readonly string[] SelfServiceVerifyPaths = ["/api/score", "/api/verify", "/api/verify-delivery"];

    /// <summary>Two requests a minute — ONE ITEM a minute, since rating one costs a GET and a POST.</summary>
    public const int CrowdPermitsPerMinute = 2;

    /// <summary>240 requests a day — 120 ITEMS, the ceiling a determined rater could reach.</summary>
    public const int CrowdPermitsPerDay = 240;

    /// <summary>The endpoints that carry <see cref="ApiTrafficClass.Crowd"/>, as one list so the budget and the
    /// paths cannot drift apart.</summary>
    public static readonly string[] CrowdPaths = ["/api/noise/crowd"];

    /// <summary>The per-IP budget for the published documents: 60/min — see <see cref="ApiTrafficClass.PublishedRead"/>.</summary>
    /// <remarks>
    /// ★ Generous rather than unlimited. These are cheap, cacheable-by-the-caller documents and the limiter is
    /// a fuse against a runaway client, not a quota — but a fuse that never blows is decoration.
    /// </remarks>
    public const int PublishedReadPermitsPerMinute = 60;

    /// <summary>The endpoints that carry <see cref="ApiTrafficClass.PublishedRead"/>, as one list so the budget
    /// and the paths cannot drift apart.</summary>
    /// <remarks>
    /// ★★ EXACTLY TWO, and deliberately not the whole read surface. <c>holdout</c>, <c>cost</c>, <c>record</c>
    /// and <c>mark</c> are also published documents and the same argument reaches them — but each is read once
    /// per period by a participant rather than once per page view by a customer, so they stay on the open
    /// budget until something is actually shown to be starved by it.
    /// </remarks>
    public static readonly string[] PublishedReadPaths = ["/api/noise/published", "/api/noise/method"];

    /// <summary>The per-IP budget for first-party browser reads: 120/min. A page view costs two calls (the version
    /// list, then one catalog), so this is roughly a reader opening the catalogue once a second all minute.</summary>
    public const int SiteReaderPermitsPerMinute = 120;

    private static readonly object CacheKey = new();

    /// <summary>
    /// Whether this looks like a page on one of our own sites reading the standard, rather than a script.
    /// <para>
    /// <c>Sec-Fetch-Site</c> is set by the browser and cannot be set by page script, and the origin must be one the
    /// island CORS policy already trusts. It is a FUSE, not a security boundary — anything can send these headers by
    /// hand — and that is fine: this only moves a caller between two per-IP budgets, both of which are still ceilings.
    /// Nothing is authorized by it, and every endpoint it reaches is anonymous and read-only in effect.
    /// </para>
    /// </summary>
    private static bool IsFirstPartyBrowserRead(HttpContext ctx)
    {
        var fetchSite = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
        if (string.IsNullOrEmpty(fetchSite) || string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            // "none" is a typed-in address bar; no header at all is a script, a proxy, or curl.
            return false;
        }

        if (string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        var allowed = ctx.RequestServices.GetRequiredService<IOptions<PublicCorsOptions>>().Value.AllowedOrigins;
        return allowed.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The request's traffic class plus the partition key its budget is accounted against
    /// (the principal for <see cref="ApiTrafficClass.Principal"/>, the client IP for the anonymous classes).</summary>
    public static (ApiTrafficClass Class, string Partition) Classify(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(CacheKey, out var cached) && cached is Classification hit)
        {
            return (hit.Class, hit.Partition);
        }

        var computed = Compute(ctx);
        ctx.Items[CacheKey] = computed;
        return (computed.Class, computed.Partition);
    }

    private static Classification Compute(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        if (!path.StartsWithSegments("/api"))
        {
            return new(ApiTrafficClass.NotApi, "");
        }

        if (ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))
        {
            return new(ApiTrafficClass.Trusted, "");
        }

        var partnerKey = ctx.RequestServices.GetRequiredService<IConfiguration>()["RateLimit:PartnerKey"];
        if (!string.IsNullOrEmpty(partnerKey) && ctx.Request.Headers["X-CAI-Partner"] == partnerKey)
        {
            return new(ApiTrafficClass.Trusted, "");
        }

        var registry = ctx.RequestServices.GetRequiredService<IOptions<RegistryOptions>>().Value;
        if (RegistryTokenAuthenticationHandler.Resolve(ctx.Request, registry) is { } principal)
        {
            // Partition by identity, never by the secret. Two principals sharing an org/name pair would share a
            // budget — harmless (the budget is a fuse, not a quota).
            return new(ApiTrafficClass.Principal, $"{principal.OrgId}/{principal.Name}");
        }

        var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (path.StartsWithSegments("/api/registry/keys") || path.StartsWithSegments("/api/registry/health"))
        {
            return new(ApiTrafficClass.RegistryPublic, clientIp);
        }

        if (SelfServiceVerifyPaths.Any(p => path.StartsWithSegments(p)))
        {
            return new(ApiTrafficClass.SelfServiceVerify, clientIp);
        }

        // ★★ The documents the standard publishes, on their own budget — see ApiTrafficClass.PublishedRead.
        if (PublishedReadPaths.Any(p => path.StartsWithSegments(p)))
        {
            return new(ApiTrafficClass.PublishedRead, clientIp);
        }

        // ★★ The public rating loop, on its own budget — see ApiTrafficClass.Crowd.
        if (CrowdPaths.Any(p => path.StartsWithSegments(p)))
        {
            return new(ApiTrafficClass.Crowd, clientIp);
        }

        return IsFirstPartyBrowserRead(ctx)
            ? new(ApiTrafficClass.SiteReader, clientIp)
            : new(ApiTrafficClass.Public, clientIp);
    }

    private sealed record Classification(ApiTrafficClass Class, string Partition);
}
