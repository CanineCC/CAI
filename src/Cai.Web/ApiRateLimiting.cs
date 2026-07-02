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

    private static readonly object CacheKey = new();

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
        return path.StartsWithSegments("/api/registry/keys") || path.StartsWithSegments("/api/registry/health")
            ? new(ApiTrafficClass.RegistryPublic, clientIp)
            : new(ApiTrafficClass.Public, clientIp);
    }

    private sealed record Classification(ApiTrafficClass Class, string Partition);
}
