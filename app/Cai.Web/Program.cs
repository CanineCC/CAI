using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Cai.Scoring;
using Cai.Web.Components;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// cai.canine.dev OWNS the rubric catalogs (the versioned, archived standard). Resolve their root from config, else the
// repo's /rubrics dir relative to the app — so it runs from a clone with no extra setup.
var rubricsRoot = builder.Configuration["Rubrics:Root"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "rubrics"));
builder.Services.AddSingleton(new RubricCatalogStore(rubricsRoot));

// Behind the dgx1 nginx reverse proxy: trust X-Forwarded-* so the rate limiter partitions by the REAL client IP.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear(); // the proxy is a different host; accept the forwarded chain (we only use it for limiting, never auth)
});

// API rate limiting (public read API): 1/second AND 3/minute AND 15/day, per client IP, chained so a request must pass
// all three. EXEMPT: loopback callers (the co-located Watchdog surveyor calls cai locally) and an optional partner key.
// Only /api is limited — the UI and the client-side WASM calculator never hit it.
var partnerKey = builder.Configuration["RateLimit:PartnerKey"];
bool Exempt(HttpContext ctx) =>
    (ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))
    || (!string.IsNullOrEmpty(partnerKey) && ctx.Request.Headers["X-CAI-Partner"] == partnerKey);

RateLimitPartition<string> Window(HttpContext ctx, string tag, int permit, TimeSpan window)
{
    if (!ctx.Request.Path.StartsWithSegments("/api") || Exempt(ctx))
    {
        return RateLimitPartition.GetNoLimiter("exempt");
    }

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter($"{tag}:{ip}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0 });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, "s", 1, TimeSpan.FromSeconds(1))),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, "m", 3, TimeSpan.FromMinutes(1))),
        PartitionedRateLimiter.Create<HttpContext, string>(ctx => Window(ctx, "d", 15, TimeSpan.FromDays(1))));
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "1";
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
        {
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "rate limit exceeded — cache the rubric you use; see https://cai.canine.dev/api-reference" }, ct);
    };
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseAntiforgery();

// ── The standard API (what the Watchdog surveyor and anyone else calls) ───────────────────────────────────────────
var api = app.MapGroup("/api");

// The published rubric versions, newest first.
api.MapGet("/rubrics", (RubricCatalogStore store) =>
    Results.Ok(new { latest = store.Latest(), versions = store.Versions() }));

// A version's full catalog — the 124 dimensions × 10 lenses it defines. "latest" resolves to the newest.
api.MapGet("/rubrics/{version}/catalog", (string version, RubricCatalogStore store) =>
{
    var resolved = version == "latest" ? store.Latest() : version;
    if (resolved is null)
    {
        return Results.NotFound(new { error = "no rubric versions published" });
    }

    var catalog = store.Get(resolved);
    return catalog is null
        ? Results.NotFound(new { error = $"unknown rubric version '{version}'", published = store.Versions() })
        : Results.Text(catalog.ToJson(), "application/json");
});

// Score an evidence bundle — the open, reproducible fold. POST the bundle JSON; get the CAI + per-lens contributions.
api.MapPost("/score", async (HttpRequest req) =>
{
    try
    {
        using var reader = new StreamReader(req.Body);
        var bundle = EvidenceBundle.Parse(await reader.ReadToEndAsync());
        var s = CaiScorer.Score(bundle);
        return Results.Ok(new
        {
            cai = Math.Round(s.Headline, 2),
            band = s.Band.Label(),
            rubricVersion = s.RubricVersion,
            lenses = s.Contributions.Select(c => new { c.Lens, c.Score, c.Weight, c.Contribution }),
        });
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

// Verify a published headline reproduces from its evidence.
api.MapPost("/verify", async (HttpRequest req) =>
{
    try
    {
        using var reader = new StreamReader(req.Body);
        var bundle = EvidenceBundle.Parse(await reader.ReadToEndAsync());
        var v = CaiScorer.Verify(bundle);
        return Results.Ok(new { reproduced = v.Reproduced, computed = Math.Round(v.Computed, 2), claimed = v.Claimed, delta = Math.Round(v.Delta, 2), tolerance = v.Tolerance });
    }
    catch (Exception e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

app.MapRazorComponents<App>();

app.Run();
